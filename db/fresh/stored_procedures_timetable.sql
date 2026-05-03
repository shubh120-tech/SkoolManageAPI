-- TIMETABLE PROCEDURE
-- Npgsql sends TimeSpan as INTERVAL; we convert to TIME (time-of-day) for the table.

CREATE OR REPLACE PROCEDURE sp_timetable_upsert(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_subject_id UUID,
    p_teacher_id UUID,
    p_day_of_week INT,
    p_start_time INTERVAL,
    p_end_time INTERVAL,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_existing_id UUID;
    v_start_time TIME := (date '2000-01-01' + p_start_time)::time;
    v_end_time TIME := (date '2000-01-01' + p_end_time)::time;
BEGIN
    SELECT id INTO v_existing_id
    FROM timetables
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND day_of_week = p_day_of_week
      AND start_time = v_start_time
      AND is_deleted = FALSE
    FOR UPDATE;

    IF FOUND THEN
        UPDATE timetables
        SET subject_id = p_subject_id,
            teacher_id = p_teacher_id,
            end_time = v_end_time,
            updated_at = NOW(),
            updated_by = p_created_by
        WHERE id = v_existing_id;
    ELSE
        INSERT INTO timetables(
            id, school_id, is_deleted, created_at, created_by,
            class_id, section_id, subject_id, teacher_id,
            day_of_week, start_time, end_time)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            p_class_id,
            p_section_id,
            p_subject_id,
            p_teacher_id,
            p_day_of_week,
            v_start_time,
            v_end_time
        );
    END IF;
END;
$$;

