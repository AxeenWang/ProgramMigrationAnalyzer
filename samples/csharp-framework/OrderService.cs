using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace LegacyOrderSystem
{
    public class OrderService
    {
        private string GetConnectionString()
        {
            return ConfigurationManager.ConnectionStrings["OrderDb"].ConnectionString;
        }

        public DataSet LoadOrder(int orderId)
        {
            DataSet dataSet = new DataSet("OrderData");
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                SqlDataAdapter headerAdapter = new SqlDataAdapter(
                    "SELECT order_id, customer_id, order_date, total_amount, status FROM orders WHERE order_id = @orderId",
                    connection);
                headerAdapter.SelectCommand.Parameters.Add("@orderId", SqlDbType.Int).Value = orderId;
                headerAdapter.Fill(dataSet, "OrderHeader");

                SqlDataAdapter detailAdapter = new SqlDataAdapter(
                    "SELECT order_id, line_no, product_id, quantity, unit_price FROM order_detail WHERE order_id = @orderId",
                    connection);
                detailAdapter.SelectCommand.Parameters.Add("@orderId", SqlDbType.Int).Value = orderId;
                detailAdapter.Fill(dataSet, "OrderDetail");
            }
            return dataSet;
        }

        public int CreateOrder(int customerId, DataTable details)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                connection.Open();
                SqlTransaction transaction = connection.BeginTransaction();
                try
                {
                    const string insertHeader = "INSERT INTO orders (customer_id, order_date, total_amount, status) VALUES (@customerId, GETDATE(), @total, 'N'); SELECT SCOPE_IDENTITY();";
                    SqlCommand headerCommand = new SqlCommand(insertHeader, connection, transaction);
                    headerCommand.Parameters.AddWithValue("@customerId", customerId);
                    headerCommand.Parameters.AddWithValue("@total", CalculateTotal(details));
                    int orderId = Convert.ToInt32(headerCommand.ExecuteScalar());

                    foreach (DataRow row in details.Rows)
                    {
                        const string insertDetail = "INSERT INTO order_detail (order_id, product_id, quantity, unit_price) VALUES (@orderId, @productId, @quantity, @price)";
                        SqlCommand detailCommand = new SqlCommand(insertDetail, connection, transaction);
                        detailCommand.Parameters.AddWithValue("@orderId", orderId);
                        detailCommand.Parameters.AddWithValue("@productId", row["ProductId"]);
                        detailCommand.Parameters.AddWithValue("@quantity", row["Quantity"]);
                        detailCommand.Parameters.AddWithValue("@price", row["UnitPrice"]);
                        detailCommand.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return orderId;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public void CancelOrder(int orderId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand("UPDATE orders SET status = 'C' WHERE order_id = @orderId", connection))
            {
                command.Parameters.AddWithValue("@orderId", orderId);
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        private static decimal CalculateTotal(DataTable details)
        {
            decimal total = 0m;
            foreach (DataRow row in details.Rows)
            {
                total += Convert.ToDecimal(row["Quantity"]) * Convert.ToDecimal(row["UnitPrice"]);
            }
            return total;
        }
    }
}
