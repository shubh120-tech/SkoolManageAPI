-- Get class teacher assignment for a staff (teacher). Returns first assignment found.
-- Run this so the staff edit form can pre-fill session, class, section for teaching staff.

CREATE OR REPLACE FUNCTION fn_class_teacher_get_by_teacher(
    p_school_id UUID,
    p_teacher_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_result JSONB;
BEGIN
    SELECT to_jsonb(row) INTO v_result
    FROM (
        SELECT
            ct.class_id AS ClassId,
            ct.section_id AS SectionId,
            ct.academic_session_id AS AcademicSessionId
        FROM class_teachers ct
        WHERE ct.school_id = p_school_id
          AND ct.teacher_id = p_teacher_id
          AND ct.is_deleted = FALSE
        LIMIT 1
    ) AS row;

    RETURN COALESCE(v_result, 'null'::jsonb);
END;
$$;
