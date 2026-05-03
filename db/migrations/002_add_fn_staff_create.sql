-- Run this on your PostgreSQL database. No procedure CALL – standalone function only (avoids Npgsql OUT parameter issue).
-- p_date_of_birth is TIMESTAMP so Npgsql DateTime maps correctly. Requires: schools and staff tables, uuid_generate_v4().

CREATE OR REPLACE FUNCTION fn_staff_create(
    p_school_id UUID,
    p_staff_code TEXT,
    p_full_name TEXT,
    p_email TEXT,
    p_date_of_birth TIMESTAMP WITHOUT TIME ZONE,
    p_mobile_no TEXT,
    p_is_teaching BOOLEAN,
    p_created_by UUID)
RETURNS TABLE(o_staff_id UUID, o_staff_code TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
    v_staff_count INT;
    v_existing INT;
    v_id UUID;
    v_code TEXT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_staff_create_' || p_school_id::text));

    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_school.is_active OR v_school.is_soft_deleted THEN
        RAISE EXCEPTION 'School invalid or inactive';
    END IF;

    IF v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW() THEN
        RAISE EXCEPTION 'School license expired';
    END IF;

    SELECT COUNT(*) INTO v_staff_count
    FROM staff
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    IF v_school.max_staff > 0 AND v_staff_count >= v_school.max_staff THEN
        RAISE EXCEPTION 'Staff limit reached for subscription';
    END IF;

    IF p_email IS NOT NULL THEN
        SELECT COUNT(*) INTO v_existing
        FROM staff
        WHERE school_id = p_school_id
          AND email = p_email
          AND is_deleted = FALSE;

        IF v_existing > 0 THEN
            RAISE EXCEPTION 'Duplicate staff email';
        END IF;
    END IF;

    INSERT INTO staff(
        id, school_id, is_deleted, created_at, created_by,
        staff_code, full_name, email, date_of_birth, mobile_no, is_teaching)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_staff_code,
        p_full_name,
        p_email,
        p_date_of_birth::date,
        p_mobile_no,
        p_is_teaching
    )
    RETURNING id, staff_code
    INTO v_id, v_code;

    o_staff_id := v_id;
    o_staff_code := v_code;
    RETURN NEXT;
END;
$$;
