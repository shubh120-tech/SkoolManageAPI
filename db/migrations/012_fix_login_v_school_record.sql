-- Migration 012: Fix PostgreSQL error 55000 "record v_school is not assigned yet" on login.
-- Cause: COALESCE(v_school.is_active, TRUE) was evaluated even when v_user.school_id IS NULL
-- (e.g. SuperAdmin), so PL/pgSQL read an uninitialized RECORD.
-- Apply: psql -U ... -d ... -f 012_fix_login_v_school_record.sql

CREATE OR REPLACE FUNCTION fn_auth_get_user_for_login(p_email TEXT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_user RECORD;
    v_school RECORD;
    v_permissions TEXT[];
BEGIN
    SELECT u.*, r.name AS role_name
    INTO v_user
    FROM users u
    JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
    WHERE LOWER(u.email) = LOWER(p_email)
      AND u.is_deleted = FALSE
    LIMIT 1;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    IF v_user.school_id IS NOT NULL THEN
        SELECT s.*
        INTO v_school
        FROM schools s
        WHERE s.id = v_user.school_id
          AND s.is_deleted = FALSE
        LIMIT 1;

        IF NOT FOUND THEN
            RETURN NULL;
        END IF;
    END IF;

    SELECT ARRAY(
        SELECT p.name
        FROM user_permissions up
        JOIN permissions p ON p.id = up.permission_id AND p.is_deleted = FALSE
        WHERE up.user_id = v_user.id AND up.is_deleted = FALSE
    ) INTO v_permissions;

    IF v_permissions IS NULL OR array_length(v_permissions, 1) IS NULL THEN
        SELECT ARRAY(
            SELECT p.name
            FROM role_permissions rp
            JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
            WHERE rp.role_id = v_user.role_id AND rp.is_deleted = FALSE
        ) INTO v_permissions;
    END IF;

    RETURN jsonb_build_object(
        'UserId', v_user.id,
        'SchoolId', v_user.school_id,
        'RoleName', v_user.role_name,
        'SchoolCode', CASE WHEN v_user.school_id IS NULL THEN NULL ELSE v_school.code END,
        'PasswordHash', v_user.password_hash,
        'IsLocked', v_user.is_locked,
        'MustChangePassword', v_user.must_change_password,
        'IsDeletedUser', v_user.is_soft_deleted_user,
        'SchoolIsActive', CASE
            WHEN v_user.school_id IS NULL THEN TRUE
            ELSE COALESCE(v_school.is_active, TRUE)
        END,
        'LicenseExpired', CASE
            WHEN v_user.school_id IS NULL THEN FALSE
            WHEN v_school.license_end_date IS NULL THEN FALSE
            ELSE v_school.license_end_date < NOW()
        END,
        'Permissions', COALESCE(v_permissions, ARRAY[]::TEXT[])
    );
END;
$$;

CREATE OR REPLACE FUNCTION fn_auth_rotate_refresh_token(p_refresh_token_hash TEXT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_rt RECORD;
    v_user RECORD;
    v_school RECORD;
    v_permissions TEXT[];
BEGIN
    SELECT *
    INTO v_rt
    FROM refresh_tokens
    WHERE token_hash = p_refresh_token_hash
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    IF v_rt.is_revoked OR v_rt.expires_at < NOW() THEN
        RETURN NULL;
    END IF;

    UPDATE refresh_tokens
    SET is_revoked = TRUE,
        updated_at = NOW()
    WHERE id = v_rt.id;

    SELECT u.*, r.name AS role_name
    INTO v_user
    FROM users u
    JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
    WHERE u.id = v_rt.user_id
      AND u.is_deleted = FALSE;

    IF NOT FOUND OR v_user.is_soft_deleted_user OR v_user.is_locked THEN
        RETURN NULL;
    END IF;

    IF v_user.school_id IS NOT NULL THEN
        SELECT * INTO v_school FROM schools s
        WHERE s.id = v_user.school_id AND s.is_deleted = FALSE;

        IF NOT FOUND OR NOT v_school.is_active
           OR (v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW()) THEN
            RETURN NULL;
        END IF;
    END IF;

    SELECT ARRAY(
        SELECT p.name
        FROM role_permissions rp
        JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
        WHERE rp.role_id = v_user.role_id AND rp.is_deleted = FALSE
    ) INTO v_permissions;

    RETURN jsonb_build_object(
        'UserId', v_user.id,
        'SchoolId', v_user.school_id,
        'RoleName', v_user.role_name,
        'SchoolCode', CASE WHEN v_user.school_id IS NULL THEN NULL ELSE v_school.code END,
        'PasswordHash', v_user.password_hash,
        'IsLocked', v_user.is_locked,
        'MustChangePassword', v_user.must_change_password,
        'IsDeletedUser', v_user.is_soft_deleted_user,
        'SchoolIsActive', CASE
            WHEN v_user.school_id IS NULL THEN TRUE
            ELSE COALESCE(v_school.is_active, TRUE)
        END,
        'LicenseExpired', CASE
            WHEN v_user.school_id IS NULL THEN FALSE
            WHEN v_school.license_end_date IS NULL THEN FALSE
            ELSE v_school.license_end_date < NOW()
        END,
        'Permissions', v_permissions
    );
END;
$$;
