-- FEES PROCEDURES WITH OVERPAYMENT AND DOUBLE PAYMENT PREVENTION

-- Signature uses TIMESTAMPTZ so Npgsql (DateTime -> timestamp with time zone) finds the procedure
CREATE OR REPLACE PROCEDURE sp_fee_record_payment(
    p_school_id UUID,
    p_student_id UUID,
    p_receipt_number TEXT,
    p_amount_paid NUMERIC(12,2),
    p_payment_date TIMESTAMP WITH TIME ZONE,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_receipt TEXT;
    v_existing INT;
    v_next_num BIGINT;
    v_total_due NUMERIC(12,2);
    v_total_paid NUMERIC(12,2);
BEGIN
    IF p_amount_paid <= 0 THEN
        RAISE EXCEPTION 'Amount paid must be positive';
    END IF;

    -- Serialize receipt allocation per school (receipt numbers are unique per school across all students).
    PERFORM pg_advisory_xact_lock(hashtext('sp_fee_payment_receipt_' || p_school_id::text));
    PERFORM pg_advisory_xact_lock(hashtext('sp_fee_record_payment_' || p_school_id::text || '_' || p_student_id::text));

    v_receipt := NULLIF(TRIM(p_receipt_number), '');
    IF v_receipt IS NULL THEN
        -- Auto-generate: REC-000001, REC-000002, ... (only considers existing REC-######## rows)
        SELECT COALESCE(MAX(
            CASE
                WHEN receipt_number ~ '^REC-[0-9]+$'
                THEN CAST(SUBSTRING(receipt_number FROM 5) AS BIGINT)
                ELSE NULL::BIGINT
            END
        ), 0) + 1
        INTO v_next_num
        FROM fee_payments
        WHERE school_id = p_school_id
          AND is_deleted = FALSE;

        v_receipt := 'REC-' || LPAD(v_next_num::TEXT, 6, '0');
    END IF;

    SELECT COUNT(*) INTO v_existing
    FROM fee_payments
    WHERE school_id = p_school_id
      AND receipt_number = v_receipt
      AND is_deleted = FALSE;

    IF v_existing > 0 THEN
        RAISE EXCEPTION 'Duplicate receipt number: % is already used for this school. Leave receipt number empty to auto-generate a new one, or enter a different receipt number.',
            v_receipt;
    END IF;

    SELECT COALESCE(SUM(amount - COALESCE(discount_amount,0) + COALESCE(late_fine_amount,0)), 0)
    INTO v_total_due
    FROM student_fee_assignments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND is_deleted = FALSE;

    SELECT COALESCE(SUM(amount_paid), 0)
    INTO v_total_paid
    FROM fee_payments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND is_deleted = FALSE;

    IF v_total_paid + p_amount_paid > v_total_due THEN
        RAISE EXCEPTION 'Overpayment detected. Due: % Paid so far: % Attempted: %',
            v_total_due, v_total_paid, p_amount_paid;
    END IF;

    INSERT INTO fee_payments(
        id, school_id, is_deleted, created_at, created_by,
        student_id, receipt_number, amount_paid, payment_date)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_student_id,
        v_receipt,
        p_amount_paid,
        p_payment_date
    );

    -- Mark fully paid assignments
    UPDATE student_fee_assignments
    SET is_paid = TRUE,
        updated_at = NOW(),
        updated_by = p_created_by
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND is_deleted = FALSE
      AND is_paid = FALSE
      AND (SELECT COALESCE(SUM(amount_paid),0)
           FROM fee_payments
           WHERE school_id = p_school_id
             AND student_id = p_student_id
             AND is_deleted = FALSE) >=
          (SELECT COALESCE(SUM(amount - COALESCE(discount_amount,0) + COALESCE(late_fine_amount,0)),0)
           FROM student_fee_assignments
           WHERE school_id = p_school_id
             AND student_id = p_student_id
             AND is_deleted = FALSE);
END;
$$;

-- FEE HEADS (MASTER)

-- Returns the created fee head as JSONB (Id, Name, IsRecurring) for use without OUT params.
CREATE OR REPLACE FUNCTION fn_fee_head_create(
    p_school_id UUID,
    p_name TEXT,
    p_is_recurring BOOLEAN,
    p_created_by UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_id UUID;
    v_exists INT;
BEGIN
    SELECT COUNT(*) INTO v_exists
    FROM fee_heads
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Fee head with this name already exists';
    END IF;

    INSERT INTO fee_heads(
        id, school_id, is_deleted, created_at, created_by,
        name, is_recurring)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_name,
        p_is_recurring
    )
    RETURNING id INTO v_id;

    RETURN jsonb_build_object('Id', v_id, 'Name', p_name, 'IsRecurring', p_is_recurring);
END;
$$;

CREATE OR REPLACE PROCEDURE sp_fee_head_create(
    p_school_id UUID,
    p_name TEXT,
    p_is_recurring BOOLEAN,
    p_created_by UUID,
    OUT o_fee_head_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_exists INT;
BEGIN
    SELECT COUNT(*) INTO v_exists
    FROM fee_heads
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Fee head with this name already exists';
    END IF;

    INSERT INTO fee_heads(
        id, school_id, is_deleted, created_at, created_by,
        name, is_recurring)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_name,
        p_is_recurring
    )
    RETURNING id INTO o_fee_head_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_fee_head_update(
    p_school_id UUID,
    p_fee_head_id UUID,
    p_name TEXT,
    p_is_recurring BOOLEAN,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_head RECORD;
    v_exists INT;
BEGIN
    SELECT *
    INTO v_head
    FROM fee_heads
    WHERE id = p_fee_head_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee head not found';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM fee_heads
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND id <> p_fee_head_id
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Fee head with this name already exists';
    END IF;

    UPDATE fee_heads
    SET name = p_name,
        is_recurring = p_is_recurring,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_fee_head_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_fee_head_soft_delete(
    p_school_id UUID,
    p_fee_head_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_head RECORD;
BEGIN
    SELECT *
    INTO v_head
    FROM fee_heads
    WHERE id = p_fee_head_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee head not found';
    END IF;

    UPDATE fee_heads
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_fee_head_id;
END;
$$;

CREATE OR REPLACE FUNCTION fn_fee_heads_get_all(
    p_school_id UUID,
    p_include_deleted BOOLEAN)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'Id', fh.id,
                'Name', fh.name,
                'IsRecurring', fh.is_recurring
            )
            ORDER BY fh.name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM fee_heads fh
    WHERE fh.school_id = p_school_id
      AND (p_include_deleted OR fh.is_deleted = FALSE);

    RETURN v_items;
END;
$$;

-- CLASS FEE STRUCTURE

CREATE OR REPLACE PROCEDURE sp_class_fee_structure_set(
    p_school_id UUID,
    p_class_id UUID,
    p_fee_head_ids UUID[],
    p_amounts NUMERIC(12,2)[],
    p_is_optional BOOLEAN[],
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_len INT;
    v_index INT;
    v_head_id UUID;
    v_amount NUMERIC(12,2);
    v_optional BOOLEAN;
BEGIN
    IF array_length(p_fee_head_ids, 1) IS NULL OR array_length(p_amounts, 1) IS NULL THEN
        RAISE EXCEPTION 'Fee heads and amounts are required';
    END IF;

    v_len := array_length(p_fee_head_ids, 1);
    IF v_len <> array_length(p_amounts, 1) THEN
        RAISE EXCEPTION 'Fee head and amount array lengths must match';
    END IF;
    IF p_is_optional IS NOT NULL AND array_length(p_is_optional, 1) IS NOT NULL AND array_length(p_is_optional, 1) <> v_len THEN
        RAISE EXCEPTION 'is_optional array length must match fee heads';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtext('sp_class_fee_structure_set_' || p_school_id::text || '_' || p_class_id::text));

    DELETE FROM class_fee_structures
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND is_deleted = FALSE;

    v_index := 1;
    WHILE v_index <= v_len LOOP
        v_head_id := p_fee_head_ids[v_index];
        v_amount := p_amounts[v_index];
        v_optional := COALESCE((p_is_optional)[v_index], FALSE);

        IF v_amount <= 0 THEN
            RAISE EXCEPTION 'Amount must be positive';
        END IF;

        PERFORM 1 FROM fee_heads
        WHERE id = v_head_id
          AND school_id = p_school_id
          AND is_deleted = FALSE;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Fee head % not found for this school', v_head_id;
        END IF;

        INSERT INTO class_fee_structures(
            id, school_id, is_deleted, created_at, created_by,
            class_id, fee_head_id, amount, is_optional)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_user_id,
            p_class_id,
            v_head_id,
            v_amount,
            v_optional
        );

        v_index := v_index + 1;
    END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION fn_class_fee_structures_get(
    p_school_id UUID,
    p_class_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'Id', cfs.id,
                'ClassId', cfs.class_id,
                'FeeHeadId', cfs.fee_head_id,
                'Amount', cfs.amount,
                'IsOptional', COALESCE(cfs.is_optional, FALSE),
                'IsRecurring', COALESCE(fh.is_recurring, TRUE),
                'AssignOnAdmission', (COALESCE(fh.is_recurring, TRUE) = FALSE)
            )
            ORDER BY cfs.fee_head_id
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM class_fee_structures cfs
    LEFT JOIN fee_heads fh
      ON fh.id = cfs.fee_head_id
     AND fh.school_id = cfs.school_id
     AND fh.is_deleted = FALSE
    WHERE cfs.school_id = p_school_id
      AND cfs.class_id = p_class_id
      AND cfs.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

-- STUDENT FEE ASSIGNMENTS (from class structure)

CREATE OR REPLACE PROCEDURE sp_student_fee_assign_from_class(
    p_school_id UUID,
    p_student_id UUID,
    p_class_id UUID,
    p_due_date DATE,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_cfs RECORD;
BEGIN
    IF p_due_date IS NULL THEN
        RAISE EXCEPTION 'Due date is required';
    END IF;

    -- Only mandatory (non-optional) fee heads. Skip if student already has this fee head for the same due date (month).
    FOR v_cfs IN
        SELECT *
        FROM class_fee_structures
        WHERE school_id = p_school_id
          AND class_id = p_class_id
          AND is_deleted = FALSE
          AND COALESCE(is_optional, FALSE) = FALSE
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM student_fee_assignments
            WHERE school_id = p_school_id
              AND student_id = p_student_id
              AND fee_head_id = v_cfs.fee_head_id
              AND due_date = p_due_date
              AND is_deleted = FALSE
        ) THEN
            INSERT INTO student_fee_assignments(
                id, school_id, is_deleted, created_at, created_by,
                student_id, fee_head_id, amount, due_date,
                discount_amount, late_fine_amount, is_paid)
            VALUES (
                uuid_generate_v4(),
                p_school_id,
                FALSE,
                NOW(),
                p_user_id,
                p_student_id,
                v_cfs.fee_head_id,
                v_cfs.amount,
                p_due_date,
                NULL,
                NULL,
                FALSE
            );
        END IF;
    END LOOP;
END;
$$;

-- Add any fee head to a student (optional/custom fee with chosen amount). No class structure required.
CREATE OR REPLACE PROCEDURE sp_student_fee_add_any(
    p_school_id UUID,
    p_student_id UUID,
    p_fee_head_id UUID,
    p_amount NUMERIC(12,2),
    p_due_date DATE,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_exists_fh INT;
    v_exists_assign INT;
BEGIN
    IF p_due_date IS NULL THEN
        RAISE EXCEPTION 'Due date is required';
    END IF;
    IF p_amount <= 0 THEN
        RAISE EXCEPTION 'Amount must be positive';
    END IF;

    SELECT COUNT(*) INTO v_exists_fh
    FROM fee_heads
    WHERE id = p_fee_head_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF v_exists_fh = 0 THEN
        RAISE EXCEPTION 'Fee head not found for this school';
    END IF;

    SELECT COUNT(*) INTO v_exists_assign
    FROM student_fee_assignments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND fee_head_id = p_fee_head_id
      AND due_date = p_due_date
      AND is_deleted = FALSE;
    IF v_exists_assign > 0 THEN
        RAISE EXCEPTION 'This fee is already assigned to the student for this month';
    END IF;

    INSERT INTO student_fee_assignments(
        id, school_id, is_deleted, created_at, created_by,
        student_id, fee_head_id, amount, due_date,
        discount_amount, late_fine_amount, is_paid)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_student_id,
        p_fee_head_id,
        p_amount,
        p_due_date,
        NULL,
        NULL,
        FALSE
    );
END;
$$;

-- Add a single optional fee (e.g. Transport) for a student from class structure.
CREATE OR REPLACE PROCEDURE sp_student_fee_add_optional(
    p_school_id UUID,
    p_student_id UUID,
    p_class_id UUID,
    p_fee_head_id UUID,
    p_due_date DATE,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_cfs RECORD;
    v_exists INT;
BEGIN
    IF p_due_date IS NULL THEN
        RAISE EXCEPTION 'Due date is required';
    END IF;

    SELECT * INTO v_cfs
    FROM class_fee_structures
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND fee_head_id = p_fee_head_id
      AND is_deleted = FALSE
      AND COALESCE(is_optional, FALSE) = TRUE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee head is not an optional fee for this class, or not found';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM student_fee_assignments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND fee_head_id = p_fee_head_id
      AND due_date = p_due_date
      AND is_deleted = FALSE;
    IF v_exists > 0 THEN
        RAISE EXCEPTION 'This optional fee is already assigned to the student for this due date';
    END IF;

    INSERT INTO student_fee_assignments(
        id, school_id, is_deleted, created_at, created_by,
        student_id, fee_head_id, amount, due_date,
        discount_amount, late_fine_amount, is_paid)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_student_id,
        v_cfs.fee_head_id,
        v_cfs.amount,
        p_due_date,
        NULL,
        NULL,
        FALSE
    );
END;
$$;

-- Update discount on a student fee assignment (scholarship, sibling discount, etc.)
-- Signature uses NUMERIC and TEXT so Npgsql CALL finds (uuid, uuid, numeric, text, uuid)
CREATE OR REPLACE PROCEDURE sp_student_fee_assignment_set_discount(
    p_school_id UUID,
    p_assignment_id UUID,
    p_discount_amount NUMERIC,
    p_discount_reason TEXT,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rec RECORD;
BEGIN
    IF p_discount_amount < 0 THEN
        RAISE EXCEPTION 'Discount amount cannot be negative';
    END IF;

    SELECT * INTO v_rec
    FROM student_fee_assignments
    WHERE id = p_assignment_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee assignment not found';
    END IF;

    IF p_discount_amount > v_rec.amount THEN
        RAISE EXCEPTION 'Discount cannot exceed fee amount';
    END IF;

    UPDATE student_fee_assignments
    SET discount_amount = NULLIF(p_discount_amount, 0),
        discount_reason = NULLIF(TRIM(p_discount_reason), ''),
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_assignment_id;
END;
$$;

-- Soft-delete a student fee assignment (e.g. remove wrongly assigned fee)
CREATE OR REPLACE PROCEDURE sp_student_fee_assignment_soft_delete(
    p_school_id UUID,
    p_assignment_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rec RECORD;
BEGIN
    SELECT * INTO v_rec
    FROM student_fee_assignments
    WHERE id = p_assignment_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee assignment not found';
    END IF;
    IF v_rec.is_paid THEN
        RAISE EXCEPTION 'Cannot remove a paid assignment';
    END IF;

    UPDATE student_fee_assignments
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_assignment_id;
END;
$$;

-- Update amount on a student fee assignment (only if not paid)
CREATE OR REPLACE PROCEDURE sp_student_fee_assignment_update_amount(
    p_school_id UUID,
    p_assignment_id UUID,
    p_amount NUMERIC(12,2),
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rec RECORD;
BEGIN
    IF p_amount <= 0 THEN
        RAISE EXCEPTION 'Amount must be positive';
    END IF;

    SELECT * INTO v_rec
    FROM student_fee_assignments
    WHERE id = p_assignment_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Fee assignment not found';
    END IF;
    IF v_rec.is_paid THEN
        RAISE EXCEPTION 'Cannot edit amount of a paid assignment';
    END IF;

    UPDATE student_fee_assignments
    SET amount = p_amount,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_assignment_id;
END;
$$;

CREATE OR REPLACE FUNCTION fn_student_fee_assignments_get(
    p_school_id UUID,
    p_student_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'Id', sfa.id,
                'StudentId', sfa.student_id,
                'FeeHeadId', sfa.fee_head_id,
                'Amount', sfa.amount,
                'DueDate', sfa.due_date,
                'DiscountAmount', sfa.discount_amount,
                'DiscountReason', sfa.discount_reason,
                'LateFineAmount', sfa.late_fine_amount,
                'IsPaid', sfa.is_paid
            )
            ORDER BY sfa.due_date, sfa.fee_head_id
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM student_fee_assignments sfa
    WHERE sfa.school_id = p_school_id
      AND sfa.student_id = p_student_id
      AND sfa.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

-- Fee summary for a student: total due, total paid, pending (for "how much is due/pending")
CREATE OR REPLACE FUNCTION fn_student_fee_summary_get(
    p_school_id UUID,
    p_student_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_total_due NUMERIC(12,2);
    v_total_paid NUMERIC(12,2);
    v_pending NUMERIC(12,2);
BEGIN
    SELECT COALESCE(SUM(
        sfa.amount - COALESCE(sfa.discount_amount, 0) + COALESCE(sfa.late_fine_amount, 0)
    ), 0)
    INTO v_total_due
    FROM student_fee_assignments sfa
    WHERE sfa.school_id = p_school_id
      AND sfa.student_id = p_student_id
      AND sfa.is_deleted = FALSE;

    SELECT COALESCE(SUM(fp.amount_paid), 0)
    INTO v_total_paid
    FROM fee_payments fp
    WHERE fp.school_id = p_school_id
      AND fp.student_id = p_student_id
      AND fp.is_deleted = FALSE;

    v_pending := GREATEST(0, v_total_due - v_total_paid);

    RETURN jsonb_build_object(
        'TotalDue', v_total_due,
        'TotalPaid', v_total_paid,
        'Pending', v_pending
    );
END;
$$;

