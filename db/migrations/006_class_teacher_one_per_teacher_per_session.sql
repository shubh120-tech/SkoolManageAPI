-- Restrict: one teacher can be class teacher for at most one class per academic session.

-- Partial unique index: same teacher cannot have more than one non-deleted assignment per session.
CREATE UNIQUE INDEX IF NOT EXISTS uq_class_teachers_teacher_per_session
ON class_teachers (school_id, teacher_id, academic_session_id)
WHERE is_deleted = FALSE;

-- Update assign procedure: update existing record by staff/session when class/section changes;
-- check target class/section is not assigned to another teacher; else insert.
CREATE OR REPLACE PROCEDURE sp_class_teacher_assign(
    p_school_id UUID,
    p_teacher_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_academic_session_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
    v_teacher RECORD;
    v_class RECORD;
    v_section RECORD;
    v_session RECORD;
    v_existing RECORD;
    v_slot_taken RECORD;
    v_constraint_name TEXT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_class_teacher_assign_' || p_school_id::text));

    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_school.is_active OR v_school.is_soft_deleted THEN
        RAISE EXCEPTION 'School invalid or inactive';
    END IF;

    IF v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW() THEN
        RAISE EXCEPTION 'School license expired';
    END IF;

    SELECT * INTO v_teacher
    FROM staff
    WHERE id = p_teacher_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Teacher not found';
    END IF;

    IF NOT v_teacher.is_teaching THEN
        RAISE EXCEPTION 'Staff is not marked as teaching';
    END IF;

    SELECT * INTO v_class
    FROM classes WHERE id = p_class_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found';
    END IF;

    SELECT * INTO v_section
    FROM sections WHERE id = p_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions WHERE id = p_academic_session_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_session.is_active THEN
        RAISE EXCEPTION 'Academic session not active';
    END IF;

    -- Existing assignment for this teacher in this session (at most one)
    SELECT id, class_id, section_id INTO v_existing
    FROM class_teachers
    WHERE school_id = p_school_id
      AND teacher_id = p_teacher_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = FALSE
    LIMIT 1;

    IF FOUND THEN
        -- Same class/section for same person: allow reallocation (no-op)
        IF v_existing.class_id = p_class_id AND v_existing.section_id = p_section_id THEN
            RETURN;
        END IF;
        -- Teacher changing class/section: allow only if target slot is free or assigned to this same teacher
        SELECT id, teacher_id INTO v_slot_taken
        FROM class_teachers
        WHERE school_id = p_school_id
          AND class_id = p_class_id
          AND section_id = p_section_id
          AND academic_session_id = p_academic_session_id
          AND is_deleted = FALSE
        LIMIT 1;
        IF FOUND AND v_slot_taken.teacher_id IS DISTINCT FROM p_teacher_id THEN
            RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
        END IF;
        IF FOUND AND v_slot_taken.teacher_id = p_teacher_id THEN
            RETURN;
        END IF;
        -- Update existing record to new class/section
        UPDATE class_teachers
        SET class_id = p_class_id,
            section_id = p_section_id,
            updated_at = NOW(),
            updated_by = p_user_id
        WHERE id = v_existing.id;
        RETURN;
    END IF;

    -- No existing assignment: check target slot is free or assigned to this same teacher (allow reallocation)
    SELECT id, teacher_id INTO v_slot_taken
    FROM class_teachers
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = FALSE
    LIMIT 1;
    IF FOUND AND v_slot_taken.teacher_id IS DISTINCT FROM p_teacher_id THEN
        RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
    END IF;
    IF FOUND AND v_slot_taken.teacher_id = p_teacher_id THEN
        RETURN;
    END IF;

    INSERT INTO class_teachers(
        id, school_id, is_deleted, created_at, created_by,
        class_id, section_id, teacher_id, academic_session_id)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_class_id,
        p_section_id,
        p_teacher_id,
        p_academic_session_id
    );
EXCEPTION
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_constraint_name = CONSTRAINT_NAME;
        IF v_constraint_name = 'uq_class_teachers_teacher_per_session' THEN
            RAISE EXCEPTION 'This teacher is already assigned as class teacher for another class in this academic session.';
        ELSE
            RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
        END IF;
END;
$$;
