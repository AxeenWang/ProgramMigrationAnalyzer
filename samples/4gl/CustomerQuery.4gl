# Customer query demonstration for Program Migration Analyzer
DATABASE legacy_crm

GLOBALS
    DEFINE g_operator CHAR(20)
END GLOBALS

MAIN
    DEFINE customer_id INTEGER
    DEFINE customer_name CHAR(80)
    DEFINE customer_status CHAR(1)
    DEFINE contact_name CHAR(80)
    DEFINE message_text CHAR(120)

    LET g_operator = "DEMO_USER"
    DISPLAY "Customer Query"
    INPUT BY NAME customer_id

    IF customer_id IS NULL OR customer_id <= 0 THEN
        DISPLAY "CustomerId cannot be empty"
        RETURN
    END IF

    CALL validate_customer(customer_id)
        RETURNING customer_status, message_text

    IF customer_status = "D" THEN
        DISPLAY "Disabled customer cannot be operated"
        RETURN
    ELSE
        CALL load_customer(customer_id)
            RETURNING customer_name, contact_name
    END IF

    IF customer_name IS NULL THEN
        DISPLAY "Customer not found"
    ELSE
        DISPLAY customer_id, customer_name, customer_status
        DISPLAY "Primary contact:", contact_name
    END IF
END MAIN

FUNCTION validate_customer(p_customer_id)
    DEFINE p_customer_id INTEGER
    DEFINE status_code CHAR(1)
    DEFINE result_message CHAR(120)

    LET status_code = NULL
    LET result_message = ""
    SELECT status INTO status_code FROM customer WHERE customer_id = p_customer_id

    IF status_code IS NULL THEN
        LET result_message = "Customer does not exist"
    ELSE
        IF status_code = "D" THEN
            LET result_message = "Customer is disabled"
        ELSE
            LET result_message = "Customer is active"
        END IF
    END IF

    RETURN status_code, result_message
END FUNCTION

FUNCTION load_customer(p_customer_id)
    DEFINE p_customer_id INTEGER
    DEFINE result_name CHAR(80)
    DEFINE result_contact CHAR(80)

    SELECT customer_name INTO result_name FROM customer WHERE customer_id = p_customer_id
    SELECT contact_name INTO result_contact FROM customer_contact WHERE customer_id = p_customer_id AND is_primary = "Y"

    IF result_contact IS NULL THEN
        LET result_contact = "No primary contact"
    END IF

    RETURN result_name, result_contact
END FUNCTION
