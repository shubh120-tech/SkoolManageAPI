-- COMMUNICATION PROCEDURES (ANNOUNCEMENTS)

CREATE OR REPLACE PROCEDURE sp_announcement_create(
    p_school_id UUID,
    p_title TEXT,
    p_message TEXT,
    p_valid_from TIMESTAMP WITH TIME ZONE,
    p_valid_to TIMESTAMP WITH TIME ZONE,
    p_for_students BOOLEAN,
    p_for_staff BOOLEAN,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_valid_to <= p_valid_from THEN
        RAISE EXCEPTION 'valid_to must be after valid_from';
    END IF;

    INSERT INTO announcements(
        id, school_id, is_deleted, created_at, created_by,
        title, message, valid_from, valid_to, for_students, for_staff)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_title,
        p_message,
        p_valid_from,
        p_valid_to,
        p_for_students,
        p_for_staff
    );
END;
$$;

