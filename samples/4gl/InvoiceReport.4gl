# Invoice report sample
DATABASE legacy_billing

MAIN
    DEFINE date_from DATE
    DEFINE date_to DATE
    DEFINE invoice_count INTEGER
    DEFINE grand_total DECIMAL(16,2)

    DISPLAY "Invoice Report"
    INPUT BY NAME date_from, date_to

    IF date_from IS NULL OR date_to IS NULL THEN
        DISPLAY "Date range is required"
        RETURN
    END IF

    CALL prepare_invoice_report(date_from, date_to)
        RETURNING invoice_count, grand_total
    DISPLAY "Invoices:", invoice_count
    DISPLAY "Grand total:", grand_total
END MAIN

REPORT invoice_report(p_invoice_id, p_customer_name, p_invoice_date, p_amount)
    DEFINE p_invoice_id INTEGER
    DEFINE p_customer_name CHAR(80)
    DEFINE p_invoice_date DATE
    DEFINE p_amount DECIMAL(14,2)

    FORMAT
        PAGE HEADER
            DISPLAY "INVOICE SUMMARY REPORT"
        ON EVERY ROW
            DISPLAY p_invoice_id, p_customer_name, p_invoice_date, p_amount
        PAGE TRAILER
            DISPLAY "END OF PAGE"
END REPORT

FUNCTION prepare_invoice_report(p_date_from, p_date_to)
    DEFINE p_date_from DATE
    DEFINE p_date_to DATE
    DEFINE invoice_id INTEGER
    DEFINE customer_name CHAR(80)
    DEFINE invoice_date DATE
    DEFINE invoice_amount DECIMAL(14,2)
    DEFINE invoice_count INTEGER
    DEFINE grand_total DECIMAL(16,2)

    LET invoice_count = 0
    LET grand_total = 0
    DECLARE invoice_cursor CURSOR FOR SELECT i.invoice_id, c.customer_name, i.invoice_date, i.total_amount FROM invoice i JOIN customer c ON c.customer_id = i.customer_id WHERE i.invoice_date >= p_date_from

    FOREACH invoice_cursor INTO invoice_id, customer_name, invoice_date, invoice_amount
        LET invoice_count = invoice_count + 1
        LET grand_total = grand_total + invoice_amount
        OUTPUT TO REPORT invoice_report(invoice_id, customer_name, invoice_date, invoice_amount)
        CALL verify_invoice_detail(invoice_id)
    END FOREACH

    FINISH REPORT invoice_report
    RETURN invoice_count, grand_total
END FUNCTION

FUNCTION verify_invoice_detail(p_invoice_id)
    DEFINE p_invoice_id INTEGER
    DEFINE detail_total DECIMAL(14,2)
    SELECT SUM(quantity * unit_price) INTO detail_total FROM invoice_detail WHERE invoice_id = p_invoice_id
    IF detail_total < 0 THEN
        DISPLAY "Invalid invoice detail total", p_invoice_id
    END IF
    RETURN detail_total
END FUNCTION
