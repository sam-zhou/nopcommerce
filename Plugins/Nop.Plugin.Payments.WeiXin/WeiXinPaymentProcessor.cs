﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Helpers;
using System.Web.Mvc;
using System.Web.Routing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.WeiXin.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;
using ZXing;
using ZXing.Common;
using Brushes = System.Windows.Media.Brushes;


namespace Nop.Plugin.Payments.WeiXin
{
    /// <summary>
    /// WeiXin payment processor
    /// </summary>
    public class WeiXinPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly WeiXinPaymentSettings _weiXinPaymentSettings;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IStoreContext _storeContext;

        private const string OrderUrl = @"https://api.mch.weixin.qq.com/pay/unifiedorder";

        #endregion

        #region Ctor

        public WeiXinPaymentProcessor(WeiXinPaymentSettings weiXinPaymentSettings,
            ISettingService settingService, IWebHelper webHelper,
            IStoreContext storeContext)
        {
            this._weiXinPaymentSettings = weiXinPaymentSettings;
            this._settingService = settingService;
            this._webHelper = webHelper;
            this._storeContext = storeContext;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets MD5 hash
        /// </summary>
        /// <param name="Input">Input</param>
        /// <param name="Input_charset">Input charset</param>
        /// <returns>Result</returns>
        public string GetMD5(string Input, string Input_charset)
        {
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] t = md5.ComputeHash(Encoding.GetEncoding(Input_charset).GetBytes(Input));
            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < t.Length; i++)
            {
                sb.Append(t[i].ToString("x").PadLeft(2, '0'));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Bubble sort
        /// </summary>
        /// <param name="Input">Input</param>
        /// <returns>Result</returns>
        public string[] BubbleSort(string[] Input)
        {
            int i, j;
            string temp;

            bool exchange;

            for (i = 0; i < Input.Length; i++)
            {
                exchange = false;

                for (j = Input.Length - 2; j >= i; j--)
                {
                    if (System.String.CompareOrdinal(Input[j + 1], Input[j]) < 0)
                    {
                        temp = Input[j + 1];
                        Input[j + 1] = Input[j];
                        Input[j] = temp;

                        exchange = true;
                    }
                }

                if (!exchange)
                {
                    break;
                }
            }
            return Input;
        }

        /// <summary>
        /// Create URL
        /// </summary>
        /// <param name="Para">Para</param>
        /// <param name="InputCharset">Input charset</param>
        /// <param name="MchId">MchId</param>
        /// <returns>Result</returns>
        public string CreatUrl(string[] Para, string InputCharset, string MchId)
        {
            int i;
            string[] Sortedstr = BubbleSort(Para);
            StringBuilder prestr = new StringBuilder();

            for (i = 0; i < Sortedstr.Length; i++)
            {
                if (i == Sortedstr.Length - 1)
                {
                    prestr.Append(Sortedstr[i]);

                }
                else
                {
                    prestr.Append(Sortedstr[i] + "&");
                }

            }

            prestr.Append(MchId);
            string sign = GetMD5(prestr.ToString(), InputCharset);
            return sign;
        }

        /// <summary>
        /// Gets HTTP
        /// </summary>
        /// <param name="StrUrl">Url</param>
        /// <param name="Timeout">Timeout</param>
        /// <returns>Result</returns>
        public string Get_Http(string StrUrl, int Timeout)
        {
            string strResult = string.Empty;
            try
            {
                HttpWebRequest myReq = (HttpWebRequest)HttpWebRequest.Create(StrUrl);
                myReq.Timeout = Timeout;
                HttpWebResponse HttpWResp = (HttpWebResponse)myReq.GetResponse();
                Stream myStream = HttpWResp.GetResponseStream();
                StreamReader sr = new StreamReader(myStream, Encoding.Default);
                StringBuilder strBuilder = new StringBuilder();
                while (-1 != sr.Peek())
                {
                    strBuilder.Append(sr.ReadLine());
                }

                strResult = strBuilder.ToString();
            }
            catch (Exception exc)
            {
                strResult = "Error: " + exc.Message;
            }
            return strResult;
        }

        public string GetQrCode(string url)
        {
            var qrWriter = new BarcodeWriter();
            qrWriter.Format = BarcodeFormat.QR_CODE;
            qrWriter.Options = new EncodingOptions {Height = 200, Width = 200, Margin = 5};

            using (var q = qrWriter.Write(url))
            {
                using (var ms = new MemoryStream())
                {
                    q.Save(ms, ImageFormat.Png);
                    return String.Format("data:image/png;base64,{0}", Convert.ToBase64String(ms.ToArray()));
                }
            }
        }



        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            Hashtable packageParameter = new Hashtable();
            packageParameter.Add("appid", _weiXinPaymentSettings.AppId);//开放账号ID  
            packageParameter.Add("mch_id", _weiXinPaymentSettings.MchId); //商户号
            //packageParameter.Add("device_info", "WEB"); //商户号
            packageParameter.Add("nonce_str", Guid.NewGuid().ToString("N")); //随机字符串
            var firstProduct = postProcessPaymentRequest.Order.OrderItems.FirstOrDefault();
            if (firstProduct != null)
            {
                packageParameter.Add("product_id", firstProduct.Product.Id.ToString());
                packageParameter.Add("body", "测试商品"); //商品描述  
            }
            else
            {
                packageParameter.Add("product_id", postProcessPaymentRequest.Order.Id.ToString());
                packageParameter.Add("body", postProcessPaymentRequest.Order.Id.ToString()); //商品描述  
            }
            
            //packageParameter.Add("detail", string.Join(", ", postProcessPaymentRequest.Order.OrderItems.Select(q => q.Product.Name))); //商品描述      
            packageParameter.Add("out_trade_no", postProcessPaymentRequest.Order.Id.ToString()); //商家订单号 
            packageParameter.Add("total_fee", ((int)(postProcessPaymentRequest.Order.OrderTotal * 100)).ToString(CultureInfo.InvariantCulture)); //商品金额,以分为单位    
            packageParameter.Add("spbill_create_ip", _webHelper.GetCurrentIpAddress()); //订单生成的机器IP，指用户浏览器端IP  
            packageParameter.Add("notify_url", "https://cn.lynexshop.com/Plugins/PaymentWeiXin/Notify"); //接收财付通通知的URL  
            packageParameter.Add("trade_type", "NATIVE");//交易类型  
            //packageParameter.Add("fee_type", "CNY"); //币种，1人民币   66  
            //packageParameter.Add("time_start", DateTime.Now.ToString("yyyyMMddHHmmss"));
            //packageParameter.Add("time_expire", DateTime.Now.AddDays(2).ToString("yyyyMMddHHmmss"));
            

            //packageParameter.Add("limit_pay", "no_credit");
            //packageParameter.Add("openid", "");
            

            //获取签名
            var sign = CreateMd5Sign("key", _weiXinPaymentSettings.OpenId, packageParameter, "utf-8");
            //拼接上签名
            packageParameter.Add("sign", sign);
            //生成加密包的XML格式字符串
            string data = ParseXML(packageParameter);
            //调用统一下单接口，获取预支付订单号码
            string prepayXml = HttpUtil.Send(data, "https://api.mch.weixin.qq.com/pay/unifiedorder");

            //获取预支付ID
            var prepayId = string.Empty;
            var xdoc = new XmlDocument();
            xdoc.LoadXml(prepayXml);
            XmlNode xn = xdoc.SelectSingleNode("xml");
            XmlNodeList xnl = xn.ChildNodes;
            if (xnl.Count > 7)
            {
                prepayId = xnl[7].InnerText;
            }

            
        }



        private string ParseXML(Hashtable parameters)
        {
            var sb = new StringBuilder();
            sb.Append("<xml>");
            var akeys = new ArrayList(parameters.Keys);
            foreach (string k in akeys)
            {
                var v = (string)parameters[k];
                if (Regex.IsMatch(v, @"^[0-9.]$"))
                {
                    sb.Append("<" + k + ">" + v + "</" + k + ">");
                }
                else
                {
                    sb.Append("<" + k + "><![CDATA[" + v + "]]></" + k + ">");
                }
            }
            sb.Append("</xml>");

            var utf8 = Encoding.UTF8;
            byte[] utfBytes = utf8.GetBytes(sb.ToString());
            var result = utf8.GetString(utfBytes, 0, utfBytes.Length);

            return result;
        }

        private string CreateMd5Sign(string key, string value, Hashtable parameters, string contentEncoding)
        {
            var sb = new StringBuilder();
            var akeys = new ArrayList(parameters.Keys);
            akeys.Sort();
            foreach (string k in akeys)
            {
                var v = (string)parameters[k];
                if (null != v && "".CompareTo(v) != 0
                    && "sign".CompareTo(k) != 0 && "key".CompareTo(k) != 0)
                {
                    sb.Append(k + "=" + v + "&");
                }
            }
            sb.Append(key + "=" + value);
            string sign = GetMD5(sb.ToString(), contentEncoding).ToUpper();
            return sign;
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _weiXinPaymentSettings.AdditionalFee;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");

            //WeiXin is the redirection payment method
            //It also validates whether order is also paid (after redirection) so customers will not be able to pay twice

            //payment status should be Pending
            if (order.PaymentStatus != PaymentStatus.Pending)
                return false;

            //let's ensure that at least 1 minute passed after order is placed
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes < 1)
                return false;

            return true;
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentWeiXin";
            routeValues = new RouteValueDictionary() { { "Namespaces", "Nop.Plugin.Payments.WeiXin.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentWeiXin";
            routeValues = new RouteValueDictionary() { { "Namespaces", "Nop.Plugin.Payments.WeiXin.Controllers" }, { "area", null } };
        }

        public Type GetControllerType()
        {
            return typeof(PaymentWeiXinController);
        }

        public override void Install()
        {
            //settings
            var settings = new WeiXinPaymentSettings()
            {
                AppId = "",
                MchId = "",
                OpenId= "",
                AdditionalFee = 0,
            };
            _settingService.SaveSetting(settings);

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.QRCode", "二维码");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.RedirectionTip", "请用微信扫描");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.AppId", "AppId");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.AppId.Hint", "Enter AppId.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.MchId", "MchId");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.MchId.Hint", "Enter MchId.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.OpenId", "OpenId");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.OpenId.Hint", "Enter OpenId.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.WeiXin.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            
            base.Install();
        }


        public override void Uninstall()
        {
            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.QRCode");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.AppId.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.AppId");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.AppId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.MchId");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.MchId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.OpenId");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.OpenId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.WeiXin.AdditionalFee.Hint");
            
            base.Uninstall();
        }
        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get
            {
                return RecurringPaymentType.NotSupported;
            }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get
            {
                return PaymentMethodType.Standard;
            }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        #endregion
    }
}
