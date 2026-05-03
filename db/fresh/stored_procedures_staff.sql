-- STAFF PROCEDURES WITH SUBSCRIPTION ENFORCEMENT

CREATE OR REPLACE PROCEDURE sp_staff_create(
    p_school_id UUID,
    p_staff_code TEXT,
    p_full_name TEXT,
    p_email TEXT,
    p_date_of_birth DATE,
    p_mobile_no TEXT,
    p_is_teaching BOOLEAN,
    p_created_by UUID,
    OUT o_staff_id UUID,
    OUT o_staff_code TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
    v_staff_count INT;
    v_existing INT;
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
        p_date_of_birth,
        p_mobile_no,
        p_is_teaching
    )
    RETURNING id, staff_code
    INTO o_staff_id, o_staff_code;
END;
$$;

-- Standalone function for Npgsql (no OUT params; SELECT * FROM fn_staff_create(...) only).
-- p_date_of_birth and p_date_of_joining are TIMESTAMP so Npgsql DateTime maps correctly; cast to date for storage.
CREATE OR REPLACE FUNCTION fn_staff_create(
    p_school_id UUID,
    p_staff_code TEXT,
    p_full_name TEXT,
    p_email TEXT,
    p_date_of_birth TIMESTAMP WITHOUT TIME ZONE,
    p_mobile_no TEXT,
    p_is_teaching BOOLEAN,
    p_created_by UUID,
    p_address_line TEXT DEFAULT NULL,
    p_date_of_joining TIMESTAMP WITHOUT TIME ZONE DEFAULT NULL)
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
        staff_code, full_name, email, date_of_birth, mobile_no, is_teaching, address_line, date_of_joining)
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
        p_is_teaching,
        p_address_line,
        p_date_of_joining::date
    )
    RETURNING id, staff_code
    INTO v_id, v_code;

    o_staff_id := v_id;
    o_staff_code := v_code;
    RETURN NEXT;
END;
$$;

-- Update staff member (p_date_of_birth and p_date_of_joining as TIMESTAMP so Npgsql DateTime maps correctly)
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

-- Soft delete staff member
CREATE OR REPLACE PROCEDURE sp_staff_soft_delete(
    p_school_id UUID,
    p_staff_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_staff RECORD;
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

    UPDATE staff
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_staff_id;
END;
$$;

-- List staff (READ)
CREATE OR REPLACE FUNCTION fn_staff_get_all(
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
                'Id', s.id,
                'StaffCode', s.staff_code,
                'FullName', s.full_name,
                'Email', s.email,
                'IsTeaching', s.is_teaching,
                'DateOfBirth', s.date_of_birth,
                'MobileNo', s.mobile_no,
                'AddressLine', s.address_line,
                'DateOfJoining', s.date_of_joining
            )
            ORDER BY s.full_name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM staff s
    WHERE s.school_id = p_school_id
      AND (p_include_deleted OR s.is_deleted = FALSE);

    RETURN v_items;
END;
$$;

-- STAFF SUBJECT ASSIGNMENT

CREATE OR REPLACE PROCEDURE sp_staff_set_subjects(
    p_school_id UUID,
    p_staff_id UUID,
    p_subject_ids UUID[],
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_staff RECORD;
    v_subject_id UUID;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_staff_set_subjects_' || p_school_id::text || '_' || p_staff_id::text));

    SELECT * INTO v_staff
    FROM staff
    WHERE id = p_staff_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Staff not found';
    END IF;

    IF NOT v_staff.is_teaching THEN
        RAISE EXCEPTION 'Staff is not marked as teaching';
    END IF;

    UPDATE staff_subjects
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE school_id = p_school_id
      AND staff_id = p_staff_id
      AND is_deleted = FALSE;

    IF p_subject_ids IS NOT NULL THEN
        FOREACH v_subject_id IN ARRAY p_subject_ids LOOP
            PERFORM 1 FROM subjects
            WHERE id = v_subject_id
              AND school_id = p_school_id
              AND is_deleted = FALSE;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Subject % not found for this school', v_subject_id;
            END IF;

            INSERT INTO staff_subjects(
                id, school_id, is_deleted, created_at, created_by,
                staff_id, subject_id)
            VALUES (
                uuid_generate_v4(),
                p_school_id,
                FALSE,
                NOW(),
                p_user_id,
                p_staff_id,
                v_subject_id
            )
            ON CONFLICT (school_id, staff_id, subject_id) DO UPDATE
            SET is_deleted = FALSE,
                updated_at = NOW(),
                updated_by = EXCLUDED.created_by;
        END LOOP;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION fn_staff_get_subjects(
    p_school_id UUID,
    p_staff_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'SubjectId', sub.id,
                'Name', sub.name,
                'Code', sub.code
            )
            ORDER BY sub.name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM staff_subjects ss
    JOIN subjects sub
      ON sub.id = ss.subject_id
     AND sub.school_id = ss.school_id
    WHERE ss.school_id = p_school_id
      AND ss.staff_id = p_staff_id
      AND ss.is_deleted = FALSE;

    RETURN v_items;
END;
$$;


