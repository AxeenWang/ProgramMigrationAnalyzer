using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace LegacyInventorySystem
{
    public class InventoryManager
    {
        private readonly string _connectionString;
        private readonly string _logFolder;
        private readonly int _defaultReorderLevel;

        public InventoryManager()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["InventoryDb"].ConnectionString;
            _logFolder = ConfigurationManager.AppSettings["InventoryLogFolder"];
            _defaultReorderLevel = Convert.ToInt32(ConfigurationManager.AppSettings["DefaultReorderLevel"]);
        }

        public InventoryStatus GetStatus(int warehouseId, int productId)
        {
            const string sql = "SELECT quantity_on_hand, reorder_level FROM inventory WHERE warehouse_id = @warehouseId AND product_id = @productId";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@warehouseId", SqlDbType.Int).Value = warehouseId;
                command.Parameters.Add("@productId", SqlDbType.Int).Value = productId;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        Log("Inventory record was not found for product " + productId);
                        return null;
                    }

                    int quantity = reader.GetInt32(0);
                    int reorderLevel = reader.IsDBNull(1) ? _defaultReorderLevel : reader.GetInt32(1);
                    return new InventoryStatus
                    {
                        WarehouseId = warehouseId,
                        ProductId = productId,
                        QuantityOnHand = quantity,
                        ReorderLevel = reorderLevel,
                        SuggestedOrderQuantity = CalculateReorderQuantity(quantity, reorderLevel)
                    };
                }
            }
        }

        public void AdjustInventory(int warehouseId, int productId, int adjustment, string reason)
        {
            const string sql = "UPDATE inventory SET quantity_on_hand = quantity_on_hand + @adjustment, modified_date = GETDATE() WHERE warehouse_id = @warehouseId AND product_id = @productId";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@adjustment", adjustment);
                command.Parameters.AddWithValue("@warehouseId", warehouseId);
                command.Parameters.AddWithValue("@productId", productId);
                connection.Open();
                int affected = command.ExecuteNonQuery();
                Log("Inventory adjustment: product=" + productId + ", quantity=" + adjustment + ", reason=" + reason + ", affected=" + affected);
            }
        }

        private static int CalculateReorderQuantity(int quantity, int reorderLevel)
        {
            if (quantity > reorderLevel)
            {
                return 0;
            }
            return Math.Max(reorderLevel * 2 - quantity, reorderLevel);
        }

        private void Log(string message)
        {
            Directory.CreateDirectory(_logFolder);
            string path = Path.Combine(_logFolder, "inventory-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
            File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
        }
    }

    public class InventoryStatus
    {
        public int WarehouseId { get; set; }
        public int ProductId { get; set; }
        public int QuantityOnHand { get; set; }
        public int ReorderLevel { get; set; }
        public int SuggestedOrderQuantity { get; set; }
    }
}
