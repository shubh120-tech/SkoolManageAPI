-- ATTENDANCE PROCEDURES (student class day, staff day, leave merge)
-- Signature matches Npgsql: timestamptz for dates, JSON string for records.
-- Student/staff JSON: prefer AttendanceStatus or Status (Present|Absent|Leave); else legacy IsPresent boolean.

CREATE OR REPLACE PROCEDURE sp_staff_attendance_merge_staff_status(
    p_school_id UUID,
    p_attendance_date TIMESTAMP WITH TIME ZONE,
    p_staff_id UUID,
    p_attendance_status TEXT,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_day_id UUID;
    v_date DATE := (p_attendance_date AT TIME ZONE 'UTC')::date;
    v_status TEXT := UPPER(TRIM(p_attendance_status));
    v_present BOOLEAN;
BEGIN
    IF v_status NOT IN ('PRESENT', 'ABSENT', 'LEAVE') THEN
        RAISE EXCEPTION 'Invalid attendance status %', p_attendance_status;
    END IF;
    v_present := (v_status IN ('PRESENT', 'LEAVE'));

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

    INSERT INTO staff_attendance_records(
        id, school_id, is_deleted, created_at, created_by,
        attendance_day_id, staff_id, is_present, attendance_status)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        v_day_id,
        p_staff_id,
        v_present,
        INITCAP(v_status)
    )
    ON CONFLICT (school_id, attendance_day_id, staff_id)
    DO UPDATE SET
        is_present = EXCLUDED.is_present,
        attendance_status = EXCLUDED.attendance_status,
        updated_at = NOW(),
        updated_by = EXCLUDED.created_by,
        is_deleted = FALSE;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_staff_attendance_delete_staff_day_if_leave(
    p_school_id UUID,
    p_attendance_date TIMESTAMP WITH TIME ZONE,
    p_staff_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_date DATE := (p_attendance_date AT TIME ZONE 'UTC')::date;
BEGIN
    DELETE FROM staff_attendance_records sar
    USING staff_attendance_days sad
    WHERE sar.attendance_day_id = sad.id
      AND sar.school_id = p_school_id
      AND sad.school_id = p_school_id
      AND sad.attendance_date = v_date
      AND sad.is_deleted = FALSE
      AND sar.staff_id = p_staff_id
      AND sar.is_deleted = FALSE
      AND sar.attendance_status = 'Leave';
END;
$$;

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
    v_status TEXT;
    v_present BOOLEAN;
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

    DELETE FROM attendance_records
    WHERE school_id = p_school_id
      AND attendance_day_id = v_day_id;

    IF v_json IS NULL OR jsonb_array_length(v_json) = 0 THEN
        RETURN;
    END IF;

    FOR v_rec IN
        SELECT
            (elem->>'StudentId')::uuid AS student_id,
            NULLIF(TRIM(COALESCE(elem->>'AttendanceStatus', elem->>'Status', '')), '') AS status_raw,
            elem->>'IsPresent' AS is_present_raw
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

        IF v_rec.status_raw IS NOT NULL THEN
            v_status := INITCAP(LOWER(v_rec.status_raw));
        ELSIF v_rec.is_present_raw IS NOT NULL THEN
            v_status := CASE WHEN (v_rec.is_present_raw)::boolean THEN 'Present' ELSE 'Absent' END;
        ELSE
            v_status := 'Present';
        END IF;

        IF v_status NOT IN ('Present', 'Absent', 'Leave') THEN
            RAISE EXCEPTION 'Invalid student attendance status %', v_status;
        END IF;

        v_present := (v_status IN ('Present', 'Leave'));

        INSERT INTO attendance_records(
            id, school_id, is_deleted, created_at, created_by,
            attendance_day_id, student_id, is_present, attendance_status)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_day_id,
            v_rec.student_id,
            v_present,
            v_status
        );
    END LOOP;
END;
$$;

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
    v_status TEXT;
    v_present BOOLEAN;
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

    DELETE FROM staff_attendance_records
    WHERE school_id = p_school_id
      AND attendance_day_id = v_day_id;

    IF v_json IS NULL OR jsonb_array_length(v_json) = 0 THEN
        RETURN;
    END IF;

    FOR v_rec IN
        SELECT
            (elem->>'StaffId')::uuid AS staff_id,
            NULLIF(TRIM(COALESCE(elem->>'AttendanceStatus', elem->>'Status', '')), '') AS status_raw,
            elem->>'IsPresent' AS is_present_raw
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

        IF v_rec.status_raw IS NOT NULL THEN
            v_status := INITCAP(LOWER(v_rec.status_raw));
        ELSIF v_rec.is_present_raw IS NOT NULL THEN
            v_status := CASE WHEN (v_rec.is_present_raw)::boolean THEN 'Present' ELSE 'Absent' END;
        ELSE
            v_status := 'Present';
        END IF;

        IF v_status NOT IN ('Present', 'Absent', 'Leave') THEN
            RAISE EXCEPTION 'Invalid staff attendance status %', v_status;
        END IF;

        v_present := (v_status IN ('Present', 'Leave'));

        INSERT INTO staff_attendance_records(
            id, school_id, is_deleted, created_at, created_by,
            attendance_day_id, staff_id, is_present, attendance_status)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_day_id,
            v_rec.staff_id,
            v_present,
            v_status
        );
    END LOOP;
END;
$$;
