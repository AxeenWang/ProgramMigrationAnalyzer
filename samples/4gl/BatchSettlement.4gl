# Nightly payment settlement batch
DATABASE legacy_finance

GLOBALS
    DEFINE g_batch_id INTEGER
    DEFINE g_error_count INTEGER
END GLOBALS

MAIN
    DEFINE process_date DATE
    DEFINE payment_id INTEGER
    DEFINE account_id INTEGER
    DEFINE payment_amount DECIMAL(14,2)
    DEFINE settlement_status CHAR(1)

    LET process_date = TODAY
    LET g_error_count = 0
    CALL create_batch(process_date)
        RETURNING g_batch_id

    DECLARE payment_cursor CURSOR FOR SELECT payment_id, account_id, amount FROM payment WHERE status = "P"
    FOREACH payment_cursor INTO payment_id, account_id, payment_amount
        CALL settle_payment(payment_id, account_id, payment_amount)
            RETURNING settlement_status
        IF settlement_status <> "S" THEN
            LET g_error_count = g_error_count + 1
        END IF
    END FOREACH

    CALL complete_batch(g_batch_id, g_error_count)
    DISPLAY "Settlement batch completed:", g_batch_id
    DISPLAY "Errors:", g_error_count
END MAIN

FUNCTION create_batch(p_process_date)
    DEFINE p_process_date DATE
    DEFINE result_batch_id INTEGER
    INSERT INTO settlement (process_date, status, success_count, error_count) VALUES (p_process_date, "R", 0, 0)
    SELECT MAX(settlement_id) INTO result_batch_id FROM settlement WHERE process_date = p_process_date
    RETURN result_batch_id
END FUNCTION

FUNCTION settle_payment(p_payment_id, p_account_id, p_amount)
    DEFINE p_payment_id INTEGER
    DEFINE p_account_id INTEGER
    DEFINE p_amount DECIMAL(14,2)
    DEFINE current_balance DECIMAL(16,2)
    DEFINE result_status CHAR(1)

    LET result_status = "F"
    BEGIN WORK
    SELECT balance INTO current_balance FROM account WHERE account_id = p_account_id

    IF current_balance IS NULL THEN
        ROLLBACK WORK
        CALL log_settlement_error(p_payment_id, "ACCOUNT_NOT_FOUND")
        RETURN result_status
    END IF

    UPDATE account SET balance = balance + p_amount WHERE account_id = p_account_id
    UPDATE payment SET status = "S", settlement_id = g_batch_id WHERE payment_id = p_payment_id

    IF SQLCA.SQLCODE <> 0 THEN
        ROLLBACK WORK
        CALL log_settlement_error(p_payment_id, "DATABASE_ERROR")
    ELSE
        COMMIT WORK
        LET result_status = "S"
    END IF
    RETURN result_status
END FUNCTION

FUNCTION log_settlement_error(p_payment_id, p_error_code)
    DEFINE p_payment_id INTEGER
    DEFINE p_error_code CHAR(40)
    INSERT INTO settlement_error (settlement_id, payment_id, error_code) VALUES (g_batch_id, p_payment_id, p_error_code)
    RETURN
END FUNCTION

FUNCTION complete_batch(p_batch_id, p_error_count)
    DEFINE p_batch_id INTEGER
    DEFINE p_error_count INTEGER
    UPDATE settlement SET status = "C", error_count = p_error_count WHERE settlement_id = p_batch_id
    RETURN
END FUNCTION
