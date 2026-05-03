-- Fee head create via function (avoids OUT param with Npgsql/Dapper).

CREATE OR REPLACE FUNCTION fn_fee_head_create(
    p_school_id UUID,
    p_name TEXT,
    p_is_recurring BOOLEAN,
    p_created_by UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_id UUID;
    v_exists INT;
BEGIN
    SELECT COUNT(*) INTO v_exists
    FROM fee_heads
    WHERE school_id = p_school_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Fee head with this name already exists';
    END IF;

    INSERT INTO fee_heads(
        id, school_id, is_deleted, created_at, created_by,
        name, is_recurring)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_name,
        p_is_recurring
    )
    RETURNING id INTO v_id;

    RETURN jsonb_build_object('Id', v_id, 'Name', p_name, 'IsRecurring', p_is_recurring);
END;
$$;
