# Warehouse inventory and reorder sample
DATABASE legacy_inventory

MAIN
    DEFINE warehouse_id INTEGER
    DEFINE product_id INTEGER
    DEFINE total_quantity INTEGER
    DEFINE reorder_count INTEGER

    DISPLAY "Inventory Check"
    INPUT BY NAME warehouse_id
    LET total_quantity = 0
    LET reorder_count = 0

    DECLARE inventory_cursor CURSOR FOR SELECT product_id, quantity_on_hand, reorder_level FROM inventory WHERE warehouse_id = warehouse_id
    FOREACH inventory_cursor INTO product_id, total_quantity, reorder_count
        CALL evaluate_stock(warehouse_id, product_id, total_quantity, reorder_count)
    END FOREACH

    CALL warehouse_summary(warehouse_id)
        RETURNING total_quantity, reorder_count
    DISPLAY "Total quantity:", total_quantity
    DISPLAY "Items below reorder level:", reorder_count
END MAIN

FUNCTION evaluate_stock(p_warehouse_id, p_product_id, p_quantity, p_reorder_level)
    DEFINE p_warehouse_id INTEGER
    DEFINE p_product_id INTEGER
    DEFINE p_quantity INTEGER
    DEFINE p_reorder_level INTEGER
    DEFINE product_name CHAR(80)
    DEFINE shortage INTEGER

    SELECT product_name INTO product_name FROM product WHERE product_id = p_product_id
    IF p_quantity <= p_reorder_level THEN
        LET shortage = p_reorder_level - p_quantity
        DISPLAY p_warehouse_id, product_name, p_quantity, p_reorder_level, shortage
        CALL create_reorder_notice(p_product_id, shortage)
    ELSE
        DISPLAY product_name, "Stock level is sufficient"
    END IF
    RETURN shortage
END FUNCTION

FUNCTION warehouse_summary(p_warehouse_id)
    DEFINE p_warehouse_id INTEGER
    DEFINE quantity_total INTEGER
    DEFINE below_level INTEGER
    DEFINE warehouse_name CHAR(80)

    SELECT warehouse_name INTO warehouse_name FROM warehouse WHERE warehouse_id = p_warehouse_id
    SELECT SUM(quantity_on_hand) INTO quantity_total FROM inventory WHERE warehouse_id = p_warehouse_id
    SELECT COUNT(*) INTO below_level FROM inventory WHERE warehouse_id = p_warehouse_id AND quantity_on_hand <= reorder_level
    DISPLAY "Warehouse:", warehouse_name
    RETURN quantity_total, below_level
END FUNCTION

FUNCTION create_reorder_notice(p_product_id, p_shortage)
    DEFINE p_product_id INTEGER
    DEFINE p_shortage INTEGER
    IF p_shortage > 0 THEN
        INSERT INTO inventory_alert (product_id, shortage_qty, created_date) VALUES (p_product_id, p_shortage, TODAY)
    END IF
    RETURN
END FUNCTION
