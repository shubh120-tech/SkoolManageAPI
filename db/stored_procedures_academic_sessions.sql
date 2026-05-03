CREATE OR REPLACE PROCEDURE sp_academic_session_create(
    p_school_id UUID,
    p_name TEXT,
    p_start_date TIMESTAMP,
    p_end_date TIMESTAMP,
    p_is_active BOOLEAN,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_exists INT;
BEGIN
    IF p_end_date <= p_start_date THEN
        RAISE EXCEPTION 'End date must be after start date';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM academic_sessions
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Academic session name already exists for this school';
    END IF;

    IF p_is_active THEN
        UPDATE academic_sessions
        SET is_active = FALSE,
            updated_at = NOW(),
            updated_by = p_created_by
        WHERE school_id = p_school_id
          AND is_deleted = FALSE;
    END IF;

    INSERT INTO academic_sessions(
        id, school_id, is_deleted, created_at, created_by,
        name, start_date, end_date, is_active)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_name,
        p_start_date,
        p_end_date,
        p_is_active
    );
END;
$$;

CREATE OR REPLACE PROCEDURE sp_academic_session_update(
    p_school_id UUID,
    p_session_id UUID,
    p_name TEXT,
    p_start_date TIMESTAMP,
    p_end_date TIMESTAMP,
    p_is_active BOOLEAN,
    p_updated_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_session RECORD;
    v_exists INT;
BEGIN
    IF p_end_date <= p_start_date THEN
        RAISE EXCEPTION 'End date must be after start date';
    END IF;

    SELECT *
    INTO v_session
    FROM academic_sessions
    WHERE id = p_session_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Academic session not found';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM academic_sessions
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND id <> p_session_id
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Academic session name already exists for this school';
    END IF;

    IF p_is_active THEN
        UPDATE academic_sessions
        SET is_active = FALSE,
            updated_at = NOW(),
            updated_by = p_updated_by
        WHERE school_id = p_school_id
          AND is_deleted = FALSE
          AND id <> p_session_id;
    END IF;

    UPDATE academic_sessions
    SET name = p_name,
        start_date = p_start_date,
        end_date = p_end_date,
        is_active = p_is_active,
        updated_at = NOW(),
        updated_by = p_updated_by
    WHERE id = p_session_id;
END;
$$;

CREATE OR REPLACE FUNCTION fn_academic_sessions_get_all(p_school_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', s.id,
            'Name', s.name,
            'StartDate', s.start_date,
            'EndDate', s.end_date,
            'IsActive', s.is_active
        )
        ORDER BY s.start_date DESC
    ), '[]'::jsonb)
    INTO v_items
    FROM academic_sessions s
    WHERE s.school_id = p_school_id
      AND s.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

CREATE OR REPLACE FUNCTION fn_academic_session_get_active(p_school_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_session RECORD;
BEGIN
    SELECT *
    INTO v_session
    FROM academic_sessions
    WHERE school_id = p_school_id
      AND is_deleted = FALSE
      AND is_active = TRUE
    ORDER BY start_date DESC
    LIMIT 1;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    RETURN jsonb_build_object(
        'Id', v_session.id,
        'Name', v_session.name,
        'StartDate', v_session.start_date,
        'EndDate', v_session.end_date,
        'IsActive', v_session.is_active
    );
END;
$$;

