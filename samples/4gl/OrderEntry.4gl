# Legacy order-entry transaction sample
DATABASE legacy_orders

MAIN
    DEFINE order_id INTEGER
    DEFINE customer_id INTEGER
    DEFINE product_id INTEGER
    DEFINE order_qty INTEGER
    DEFINE unit_price DECIMAL(12,2)
    DEFINE order_total DECIMAL(14,2)
    DEFINE validation_message CHAR(120)

    DISPLAY "New Order Entry"
    INPUT BY NAME customer_id, product_id, order_qty

    CALL validate_order(customer_id, product_id, order_qty)
        RETURNING validation_message

    IF validation_message <> "OK" THEN
        DISPLAY validation_message
        RETURN
    END IF

    CALL get_product_price(product_id)
        RETURNING unit_price
    LET order_total = unit_price * order_qty

    BEGIN WORK
    INSERT INTO orders (customer_id, order_date, total_amount, status) VALUES (customer_id, TODAY, order_total, "N")
    SELECT MAX(order_id) INTO order_id FROM orders WHERE customer_id = customer_id
    INSERT INTO order_detail (order_id, product_id, quantity, unit_price) VALUES (order_id, product_id, order_qty, unit_price)
    UPDATE customer SET last_order_date = TODAY WHERE customer_id = customer_id

    IF SQLCA.SQLCODE <> 0 THEN
        ROLLBACK WORK
        DISPLAY "Order creation failed"
        RETURN
    ELSE
        COMMIT WORK
        DISPLAY "Order created:", order_id
    END IF
END MAIN

FUNCTION validate_order(p_customer_id, p_product_id, p_qty)
    DEFINE p_customer_id INTEGER
    DEFINE p_product_id INTEGER
    DEFINE p_qty INTEGER
    DEFINE customer_status CHAR(1)
    DEFINE product_status CHAR(1)
    DEFINE message_text CHAR(120)

    LET message_text = "OK"
    IF p_qty <= 0 THEN
        RETURN "Quantity must be greater than zero"
    END IF

    SELECT status INTO customer_status FROM customer WHERE customer_id = p_customer_id
    IF customer_status IS NULL THEN
        RETURN "Customer not found"
    END IF
    IF customer_status = "D" THEN
        RETURN "Disabled customer cannot place orders"
    END IF

    SELECT status INTO product_status FROM product WHERE product_id = p_product_id
    IF product_status <> "A" THEN
        RETURN "Product is unavailable"
    END IF

    RETURN message_text
END FUNCTION

FUNCTION get_product_price(p_product_id)
    DEFINE p_product_id INTEGER
    DEFINE result_price DECIMAL(12,2)
    SELECT unit_price INTO result_price FROM product WHERE product_id = p_product_id
    RETURN result_price
END FUNCTION
