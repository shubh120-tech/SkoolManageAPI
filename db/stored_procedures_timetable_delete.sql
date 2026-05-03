-- TIMETABLE DELETE PROCEDURE

CREATE OR REPLACE PROCEDURE sp_timetable_delete(
    p_school_id UUID,
    p_timetable_id UUID,
    p_deleted_by UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    -- Hard delete the timetable slot so the unique constraint on
    -- (school_id, class_id, section_id, day_of_week, start_time) is released.
    DELETE FROM timetables
    WHERE id = p_timetable_id
      AND school_id = p_school_id;
END;
$$;

