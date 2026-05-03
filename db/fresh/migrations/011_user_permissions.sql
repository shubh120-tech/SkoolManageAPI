-- Migration 011: user_permissions table and fn_auth_get_user_for_login update
-- Run this file against your PostgreSQL database once (e.g. psql -U postgres -d YourDbName -f 011_user_permissions.sql)
-- User-level permission overrides (SchoolAdmin assigns permissions to Staff/Teachers).
-- When a user has rows here, login uses this set instead of role_permissions.

CREATE TABLE IF NOT EXISTS user_permissions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    user_id UUID NOT NULL REFERENCES users(id),
    permission_id UUID NOT NULL REFERENCES permissions(id),
    CONSTRAINT uq_user_permissions UNIQUE (user_id, permission_id)
);

CREATE INDEX IF NOT EXISTS ix_user_permissions_user_id
    ON user_permissions(user_id) WHERE is_deleted = FALSE;

COMMENT ON TABLE user_permissions IS 'Per-user permission overrides; used when SchoolAdmin assigns permissions to staff/teachers.';

-- Use user_permissions for login when set; otherwise fall back to role_permissions.
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

    -- Prefer user-level permissions (admin-assigned) over role permissions
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
