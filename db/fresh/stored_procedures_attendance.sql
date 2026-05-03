-- ATTENDANCE PROCEDURES WITH DUPLICATE PREVENTION
-- Signature matches Npgsql: 4th param timestamptz (DateTime), 6th param text (JSON string).

CREATE OR REPLACE PROCEDURE sp_attendance_mark_class_day(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_attendance_date TIMESTAMP WITH TIME ZONE,
    p_created_by UUID,
    p_records_json TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_day_id UUID;
    v_rec RECORD;
    v_student_exists BOOLEAN;
    v_date DATE := (p_attendance_date AT TIME ZONE 'UTC')::date;
    v_json JSONB := CASE WHEN p_records_json IS NULL OR TRIM(p_records_json) = '' THEN '[]'::jsonb ELSE p_records_json::jsonb END;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_attendance_mark_' || p_school_id::text || '_' || p_class_id::text || '_' || p_section_id::text || '_' || v_date::text));

    SELECT id INTO v_day_id
    FROM attendance_days
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND attendance_date = v_date
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        INSERT INTO attendance_days(
            id, school_id, is_deleted, created_at, created_by,
            class_id, section_id, attendance_date)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            p_class_id,
            p_section_id,
            v_date
        )
        RETURNING id INTO v_day_id;
    END IF;

    -- clear existing records to prevent duplicates on re-submit
    DELETE FROM attendance_records
    WHERE school_id = p_school_id
      AND attendance_day_id = v_day_id;

    IF v_json IS NULL OR jsonb_array_length(v_json) = 0 THEN
        RETURN;
    END IF;

    FOR v_rec IN
        SELECT
            (elem->>'StudentId')::uuid AS student_id,
            (elem->>'IsPresent')::boolean AS is_present
        FROM jsonb_array_elements(v_json) elem
    LOOP
        SELECT EXISTS(
            SELECT 1 FROM students
            WHERE id = v_rec.student_id
              AND school_id = p_school_id
              AND is_deleted = FALSE
        ) INTO v_student_exists;

        IF NOT v_student_exists THEN
            RAISE EXCEPTION 'Student % not found for school %', v_rec.student_id, p_school_id;
        END IF;

        INSERT INTO attendance_records(
            id, school_id, is_deleted, created_at, created_by,
            attendance_day_id, student_id, is_present)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_day_id,
            v_rec.student_id,
            v_rec.is_present
        );
    END LOOP;
END;
$$;

-- Staff daily attendance (per day, all staff) using JSON payload.

CREATE OR REPLACE PROCEDURE sp_staff_attendance_mark_day(
    p_school_id UUID,
    p_attendance_date TIMESTAMP WITH TIME ZONE,
    p_created_by UUID,
    p_records_json TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_day_id UUID;
    v_rec RECORD;
    v_staff_exists BOOLEAN;
    v_date DATE := (p_attendance_date AT TIME ZONE 'UTC')::date;
    v_json JSONB := CASE WHEN p_records_json IS NULL OR TRIM(p_records_json) = '' THEN '[]'::jsonb ELSE p_records_json::jsonb END;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_staff_attendance_mark_' || p_school_id::text || '_' || v_date::text));

    SELECT id INTO v_day_id
    FROM staff_attendance_days
    WHERE school_id = p_school_id
      AND attendance_date = v_date
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        INSERT INTO staff_attendance_days(
            id, school_id, is_deleted, created_at, created_by,
            attendance_date)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_date
        )
        RETURNING id INTO v_day_id;
    END IF;

    -- clear existing records to prevent duplicates on re-submit
    DELETE FROM staff_attendance_records
    WHERE school_id = p_school_id
      AND attendance_day_id = v_day_id;

    IF v_json IS NULL OR jsonb_array_length(v_json) = 0 THEN
        RETURN;
    END IF;

    FOR v_rec IN
        SELECT
            (elem->>'StaffId')::uuid AS staff_id,
            (elem->>'IsPresent')::boolean AS is_present
        FROM jsonb_array_elements(v_json) elem
    LOOP
        SELECT EXISTS(
            SELECT 1 FROM staff
            WHERE id = v_rec.staff_id
              AND school_id = p_school_id
              AND is_deleted = FALSE
        ) INTO v_staff_exists;

        IF NOT v_staff_exists THEN
            RAISE EXCEPTION 'Staff % not found for school %', v_rec.staff_id, p_school_id;
        END IF;

        INSERT INTO staff_attendance_records(
            id, school_id, is_deleted, created_at, created_by,
            attendance_day_id, staff_id, is_present)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_day_id,
            v_rec.staff_id,
            v_rec.is_present
        );
    END LOOP;
END;
$$;

