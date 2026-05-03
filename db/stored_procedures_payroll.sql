-- PAYROLL PROCEDURE WITH DUPLICATE PREVENTION

CREATE OR REPLACE PROCEDURE sp_payroll_generate(
    p_school_id UUID,
    p_staff_id UUID,
    p_month INT,
    p_year INT,
    p_generated_on TIMESTAMP WITH TIME ZONE,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_salary RECORD;
    v_existing INT;
    v_gross NUMERIC(12,2);
    v_net NUMERIC(12,2);
    v_prev_pending NUMERIC(12,2) := 0;
    v_total_due NUMERIC(12,2);
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_payroll_generate_' || p_school_id::text || '_' || p_staff_id::text || '_' || p_year::text || '_' || p_month::text));

    SELECT COUNT(*) INTO v_existing
    FROM payroll_records
    WHERE school_id = p_school_id
      AND staff_id = p_staff_id
      AND month = p_month
      AND year = p_year
      AND is_deleted = FALSE;

    IF v_existing > 0 THEN
        RAISE EXCEPTION 'Payroll already generated for staff/month/year';
    END IF;

    SELECT * INTO v_salary
    FROM salary_structures
    WHERE staff_id = p_staff_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Salary structure not found';
    END IF;

    -- Get previous pending (from last non-deleted payroll record, if any)
    SELECT pending_amount
    INTO v_prev_pending
    FROM payroll_records
    WHERE school_id = p_school_id
      AND staff_id = p_staff_id
      AND is_deleted = FALSE
      AND (year < p_year OR (year = p_year AND month < p_month))
    ORDER BY year DESC, month DESC
    LIMIT 1;

    IF v_prev_pending IS NULL THEN
        v_prev_pending := 0;
    END IF;

    v_gross := COALESCE(v_salary.basic, 0) + COALESCE(v_salary.allowances, 0);
    v_net := v_gross - COALESCE(v_salary.deductions, 0);
    v_total_due := v_prev_pending + v_net;

    INSERT INTO payroll_records(
        id, school_id, is_deleted, created_at, created_by,
        staff_id, month, year, gross_amount, net_amount, generated_on,
        previous_pending, total_due, total_paid, pending_amount)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_staff_id,
        p_month,
        p_year,
        v_gross,
        v_net,
        p_generated_on,
        v_prev_pending,
        v_total_due,
        0,
        v_total_due
    );
END;
$$;

-- Record a staff payroll payment and update pending amounts.

CREATE OR REPLACE PROCEDURE sp_staff_payment_add(
    p_school_id UUID,
    p_payroll_record_id UUID,
    p_staff_id UUID,
    p_amount_paid NUMERIC(12,2),
    p_payment_date TIMESTAMP WITH TIME ZONE,
    p_payment_mode VARCHAR(50),
    p_reference_no VARCHAR(100),
    p_remarks TEXT,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rows INT;
BEGIN
    IF p_amount_paid <= 0 THEN
        RAISE EXCEPTION 'Payment amount must be positive';
    END IF;

    -- Update payroll record first; if no row matches, do not insert payment.
    UPDATE payroll_records
    SET total_paid = total_paid + p_amount_paid,
        pending_amount = GREATEST(total_due - (total_paid + p_amount_paid), 0),
        updated_at = NOW(),
        updated_by = p_created_by
    WHERE id = p_payroll_record_id
      AND school_id = p_school_id
      AND staff_id = p_staff_id
      AND is_deleted = FALSE;

    GET DIAGNOSTICS v_rows = ROW_COUNT;
    IF v_rows = 0 THEN
        RAISE EXCEPTION 'Payroll record not found or does not match staff/school. Ensure payroll is generated for this staff and month.';
    END IF;

    INSERT INTO staff_payments(
        id, school_id, is_deleted, created_at, created_by,
        payroll_record_id, staff_id, amount_paid, payment_date,
        payment_mode, reference_no, remarks)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_payroll_record_id,
        p_staff_id,
        p_amount_paid,
        p_payment_date,
        p_payment_mode,
        NULLIF(p_reference_no, ''),
        NULLIF(p_remarks, '')
    );
END;
$$;

-- Bulk generate payroll for all staff with salary structures for a given month/year.

CREATE OR REPLACE PROCEDURE sp_payroll_generate_all(
    p_school_id UUID,
    p_month INT,
    p_year INT,
    p_generated_on TIMESTAMP WITH TIME ZONE,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN
        SELECT DISTINCT staff_id
        FROM salary_structures
        WHERE school_id = p_school_id
          AND is_deleted = FALSE
    LOOP
        BEGIN
            CALL sp_payroll_generate(
                p_school_id,
                r.staff_id,
                p_month,
                p_year,
                p_generated_on,
                p_created_by
            );
        EXCEPTION
            WHEN others THEN
                -- Ignore errors for individual staff (e.g. already generated).
                CONTINUE;
        END;
    END LOOP;
END;
$$;

