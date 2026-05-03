-- AUTH FUNCTIONS AND PROCEDURES

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
        -- Must not reference v_school when school_id IS NULL (e.g. SuperAdmin); unassigned RECORD errors in PG.
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

CREATE OR REPLACE PROCEDURE sp_auth_register_failed_login(p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_user RECORD;
BEGIN
    SELECT * INTO v_user FROM users WHERE id = p_user_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'User not found';
    END IF;

    IF v_user.is_locked THEN
        RETURN;
    END IF;

    UPDATE users
    SET failed_login_attempts = failed_login_attempts + 1,
        is_locked = (failed_login_attempts + 1) >= 5,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_user_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_auth_register_successful_login(p_user_id UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE users
    SET failed_login_attempts = 0,
        is_locked = FALSE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_user_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_auth_create_refresh_token(
    p_user_id UUID,
    p_token_hash TEXT,
    p_expires_at TIMESTAMP)
LANGUAGE plpgsql
AS $$
DECLARE
    v_user RECORD;
BEGIN
    SELECT * INTO v_user FROM users WHERE id = p_user_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'User not found';
    END IF;

    INSERT INTO refresh_tokens(
        id, school_id, is_deleted, created_at, created_by,
        user_id, token_hash, expires_at, is_revoked)
    VALUES (
        uuid_generate_v4(),
        v_user.school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_user_id,
        p_token_hash,
        p_expires_at,
        FALSE
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

CREATE OR REPLACE PROCEDURE sp_auth_revoke_refresh_token(p_refresh_token_hash TEXT)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE refresh_tokens
    SET is_revoked = TRUE,
        updated_at = NOW()
    WHERE token_hash = p_refresh_token_hash
      AND is_revoked = FALSE;
END;
$$;

CREATE OR REPLACE FUNCTION fn_auth_get_user_password_hash(p_user_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_user RECORD;
BEGIN
    SELECT * INTO v_user FROM users WHERE id = p_user_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    RETURN jsonb_build_object(
        'PasswordHash', v_user.password_hash
    );
END;
$$;

CREATE OR REPLACE PROCEDURE sp_auth_change_password(
    p_user_id UUID,
    p_new_password_hash TEXT)
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM 1 FROM users WHERE id = p_user_id AND is_deleted = FALSE FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'User not found';
    END IF;

    UPDATE users
    SET password_hash = p_new_password_hash,
        must_change_password = FALSE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_user_id;

    UPDATE refresh_tokens
    SET is_revoked = TRUE,
        updated_at = NOW()
    WHERE user_id = p_user_id
      AND is_revoked = FALSE;
END;
$$;

-- For demo purposes, reset-token handling is simplified but functional.

CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id),
    token VARCHAR(200) NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    used BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE OR REPLACE PROCEDURE sp_auth_generate_reset_password_token(p_email TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_user_id UUID;
    v_token TEXT;
BEGIN
    SELECT id INTO v_user_id FROM users WHERE LOWER(email) = LOWER(p_email) AND is_deleted = FALSE LIMIT 1;
    IF NOT FOUND THEN
        RETURN;
    END IF;

    v_token := encode(gen_random_bytes(32), 'hex');

    INSERT INTO password_reset_tokens(user_id, token, expires_at, used)
    VALUES (v_user_id, v_token, NOW() + INTERVAL '1 hour', FALSE);
END;
$$;

CREATE OR REPLACE PROCEDURE sp_auth_reset_password(
    p_user_id UUID,
    p_token TEXT,
    p_new_password_hash TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_prt RECORD;
BEGIN
    SELECT *
    INTO v_prt
    FROM password_reset_tokens
    WHERE user_id = p_user_id
      AND token = p_token
      AND used = FALSE
      AND expires_at > NOW()
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    UPDATE users
    SET password_hash = p_new_password_hash,
        must_change_password = FALSE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_user_id;

    UPDATE password_reset_tokens
    SET used = TRUE
    WHERE id = v_prt.id;

    UPDATE refresh_tokens
    SET is_revoked = TRUE,
        updated_at = NOW()
    WHERE user_id = p_user_id
      AND is_revoked = FALSE;
END;
$$;

-- AUDIT

CREATE OR REPLACE PROCEDURE sp_audit_log_create(
    p_school_id UUID,
    p_user_id UUID,
    p_action TEXT,
    p_entity_name TEXT,
    p_entity_id UUID,
    p_details_json TEXT)
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO audit_logs(
        id, school_id, is_deleted, created_at, created_by,
        user_id, action, entity_name, entity_id, details_json, ip_address)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        COALESCE(p_user_id, '00000000-0000-0000-0000-000000000000'::uuid),
        p_user_id,
        p_action,
        p_entity_name,
        p_entity_id,
        p_details_json,
        NULL
    );
END;
$$;

-- SCHOOL STATUS AND LICENSE CHECKS

CREATE OR REPLACE FUNCTION fn_school_check_status(p_school_id UUID)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
BEGIN
    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RETURN 'DELETED';
    END IF;

    IF v_school.is_soft_deleted OR NOT v_school.is_active THEN
        RETURN 'INACTIVE';
    END IF;

    RETURN 'ACTIVE';
END;
$$;

CREATE OR REPLACE FUNCTION fn_school_check_license(p_school_id UUID)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
BEGIN
    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RETURN FALSE;
    END IF;

    IF v_school.license_end_date IS NULL THEN
        RETURN TRUE;
    END IF;

    RETURN v_school.license_end_date >= NOW();
END;
$$;

