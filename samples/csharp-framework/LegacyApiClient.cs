using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace LegacyIntegration
{
    public class LegacyApiClient
    {
        private readonly string _serviceUrl;
        private readonly string _userName;
        private readonly string _password;

        public LegacyApiClient()
        {
            _serviceUrl = ConfigurationManager.AppSettings["LegacyApiUrl"];
            _userName = ConfigurationManager.AppSettings["LegacyApiUser"];
            _password = ConfigurationManager.AppSettings["LegacyApiPassword"];
        }

        public CustomerResponse GetCustomer(int customerId)
        {
            string requestUrl = _serviceUrl + "/customer?id=" + customerId;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUrl);
            request.Method = "GET";
            request.Accept = "application/xml";
            request.Credentials = new NetworkCredential(_userName, _password);
            request.Timeout = 30000;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                string xml = reader.ReadToEnd();
                return Deserialize<CustomerResponse>(xml);
            }
        }

        public ApiResult UpdateCustomer(CustomerRequest customer)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_serviceUrl + "/customer/update");
            request.Method = "POST";
            request.ContentType = "application/xml";
            request.Credentials = new NetworkCredential(_userName, _password);

            string payload = Serialize(customer);
            byte[] buffer = Encoding.UTF8.GetBytes(payload);
            request.ContentLength = buffer.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(buffer, 0, buffer.Length);
            }

            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(reader.ReadToEnd());
                XmlNode status = document.SelectSingleNode("/Result/Status");
                XmlNode message = document.SelectSingleNode("/Result/Message");
                return new ApiResult
                {
                    Success = status != null && status.InnerText == "OK",
                    Message = message == null ? string.Empty : message.InnerText
                };
            }
        }

        private static T Deserialize<T>(string xml)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));
            using (StringReader reader = new StringReader(xml))
            {
                return (T)serializer.Deserialize(reader);
            }
        }

        private static string Serialize<T>(T value)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));
            using (StringWriter writer = new StringWriter())
            {
                serializer.Serialize(writer, value);
                return writer.ToString();
            }
        }
    }

    public class CustomerRequest
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
    }

    public class CustomerResponse
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
    }

    public class ApiResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
