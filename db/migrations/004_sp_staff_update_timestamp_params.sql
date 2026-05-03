-- Fix sp_staff_update signature so Npgsql DateTime (sent as timestamp) matches.
-- Run this if staff update fails with "procedure sp_staff_update(...) does not exist".

CREATE OR REPLACE PROCEDURE sp_staff_update(
    p_school_id UUID,
    p_staff_id UUID,
    p_staff_code TEXT,
    p_full_name TEXT,
    p_email TEXT,
    p_date_of_birth TIMESTAMP WITHOUT TIME ZONE,
    p_mobile_no TEXT,
    p_is_teaching BOOLEAN,
    p_user_id UUID,
    p_address_line TEXT DEFAULT NULL,
    p_date_of_joining TIMESTAMP WITHOUT TIME ZONE DEFAULT NULL)
LANGUAGE plpgsql
AS $$
DECLARE
    v_staff RECORD;
    v_existing INT;
BEGIN
    SELECT *
    INTO v_staff
    FROM staff
    WHERE id = p_staff_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Staff not found';
    END IF;

    IF p_email IS NOT NULL THEN
        SELECT COUNT(*)
        INTO v_existing
        FROM staff
        WHERE school_id = p_school_id
          AND email = p_email
          AND id <> p_staff_id
          AND is_deleted = FALSE;

        IF v_existing > 0 THEN
            RAISE EXCEPTION 'Duplicate staff email';
        END IF;
    END IF;

    UPDATE staff
    SET staff_code  = p_staff_code,
        full_name   = p_full_name,
        email       = p_email,
        date_of_birth = p_date_of_birth::date,
        mobile_no   = p_mobile_no,
        is_teaching = p_is_teaching,
        address_line = p_address_line,
        date_of_joining = p_date_of_joining::date,
        updated_at  = NOW(),
        updated_by  = p_user_id
    WHERE id = p_staff_id;
END;
$$;
