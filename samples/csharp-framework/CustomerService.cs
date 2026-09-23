using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace LegacyCustomerSystem
{
    public class CustomerService
    {
        private readonly string _connectionString;

        public CustomerService()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["LegacyDb"].ConnectionString;
        }

        public CustomerRecord GetCustomer(int customerId)
        {
            const string sql = "SELECT customer_id, customer_name, status, credit_limit FROM customer WHERE customer_id = @customerId";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@customerId", SqlDbType.Int).Value = customerId;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return MapCustomer(reader);
                }
            }
        }

        public IList<CustomerRecord> FindCustomers(string keyword)
        {
            var result = new List<CustomerRecord>();
            const string sql = "SELECT customer_id, customer_name, status, credit_limit FROM customer WHERE customer_name LIKE @keyword ORDER BY customer_name";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@keyword", SqlDbType.NVarChar, 80).Value = "%" + keyword + "%";
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(MapCustomer(reader));
                    }
                }
            }
            return result;
        }

        public bool IsCustomerActive(int customerId)
        {
            CustomerRecord customer = GetCustomer(customerId);
            return customer != null && !string.Equals(customer.Status, "D", StringComparison.OrdinalIgnoreCase);
        }

        private static CustomerRecord MapCustomer(SqlDataReader reader)
        {
            return new CustomerRecord
            {
                CustomerId = reader.GetInt32(0),
                CustomerName = reader.GetString(1),
                Status = reader.GetString(2),
                CreditLimit = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3)
            };
        }
    }

    public class CustomerRecord
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
        public decimal CreditLimit { get; set; }
    }
}
