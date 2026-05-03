-- Allow one teacher to be class teacher for multiple classes (one-to-many).
-- Keep: one class (class+section+session) has only one class teacher (uq_class_teachers).

-- Remove the restriction that limited a teacher to one class per session.
DROP INDEX IF EXISTS uq_class_teachers_teacher_per_session;

-- Assign procedure: allow same teacher to have multiple class assignments.
-- Rule: (class, section, session) can have at most one teacher; one teacher can have many such assignments.
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
    v_slot RECORD;
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
    IF v_class.academic_session_id IS DISTINCT FROM p_academic_session_id THEN
        RAISE EXCEPTION 'Class does not belong to the selected academic session';
    END IF;

    SELECT * INTO v_section
    FROM sections WHERE id = p_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;
    IF v_section.class_id IS DISTINCT FROM p_class_id OR v_section.academic_session_id IS DISTINCT FROM p_academic_session_id THEN
        RAISE EXCEPTION 'Section does not belong to the selected class/session';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions WHERE id = p_academic_session_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_session.is_active THEN
        RAISE EXCEPTION 'Academic session not active';
    END IF;

    -- Check if this (class, section, session) is already assigned (non-deleted)
    SELECT id, teacher_id INTO v_slot
    FROM class_teachers
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = FALSE
    LIMIT 1;

    IF FOUND THEN
        IF v_slot.teacher_id = p_teacher_id THEN
            RETURN;
        END IF;
        RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
    END IF;

    -- Reactivate a soft-deleted row for this slot if it exists (unique constraint includes deleted rows)
    UPDATE class_teachers
    SET is_deleted = FALSE,
        teacher_id = p_teacher_id,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = TRUE;

    IF FOUND THEN
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
        RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
END;
$$;
