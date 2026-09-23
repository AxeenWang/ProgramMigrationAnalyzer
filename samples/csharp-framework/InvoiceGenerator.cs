using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace LegacyInvoiceSystem
{
    public class InvoiceGenerator
    {
        public string ExportInvoice(DataTable invoiceHeader, DataTable invoiceLines, string outputFolder)
        {
            if (invoiceHeader.Rows.Count == 0)
            {
                throw new InvalidOperationException("Invoice header is required.");
            }

            DataRow header = invoiceHeader.Rows[0];
            string invoiceNumber = Convert.ToString(header["InvoiceNumber"]);
            string outputPath = Path.Combine(outputFolder, invoiceNumber + ".xml");
            Directory.CreateDirectory(outputFolder);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };

            using (XmlWriter writer = XmlWriter.Create(outputPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("Invoice");
                writer.WriteAttributeString("number", invoiceNumber);
                writer.WriteElementString("CustomerId", Convert.ToString(header["CustomerId"]));
                writer.WriteElementString("InvoiceDate", Convert.ToDateTime(header["InvoiceDate"]).ToString("yyyy-MM-dd"));
                writer.WriteStartElement("Lines");

                decimal total = 0m;
                foreach (DataRow line in invoiceLines.Rows)
                {
                    decimal quantity = Convert.ToDecimal(line["Quantity"]);
                    decimal unitPrice = Convert.ToDecimal(line["UnitPrice"]);
                    decimal amount = quantity * unitPrice;
                    total += amount;

                    writer.WriteStartElement("Line");
                    writer.WriteElementString("ProductId", Convert.ToString(line["ProductId"]));
                    writer.WriteElementString("Description", Convert.ToString(line["Description"]));
                    writer.WriteElementString("Quantity", quantity.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("UnitPrice", unitPrice.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("Amount", amount.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteElementString("TotalAmount", total.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            WriteAuditFile(outputFolder, invoiceNumber, invoiceLines.Rows.Count);
            return outputPath;
        }

        private static void WriteAuditFile(string outputFolder, string invoiceNumber, int lineCount)
        {
            string path = Path.Combine(outputFolder, "invoice-export.log");
            using (StreamWriter writer = new StreamWriter(path, true, Encoding.Default))
            {
                writer.WriteLine(
                    DateTime.Now.ToString("s") + "|" +
                    invoiceNumber + "|" +
                    lineCount);
            }
        }

        public string ReadInvoiceStatus(string xmlPath)
        {
            XmlDocument document = new XmlDocument();
            document.Load(xmlPath);
            XmlNode status = document.SelectSingleNode("/Invoice/Status");
            return status == null ? "UNKNOWN" : status.InnerText;
        }
    }
}
