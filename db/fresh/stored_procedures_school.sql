-- SCHOOL AND SUBSCRIPTION PROCEDURES

-- Register a new school and its initial admin user inside a transaction.
-- Enforces unique school code and email, and creates base roles if needed.

CREATE OR REPLACE PROCEDURE sp_school_register(
    p_code TEXT,
    p_name TEXT,
    p_admin_name TEXT,
    p_admin_email TEXT,
    p_admin_password_hash TEXT,
    p_contact_phone TEXT,
    p_address_line TEXT,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_school_id UUID;
    v_admin_role_id UUID;
    v_existing_school INT;
    v_existing_user INT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_school_register'));

    SELECT COUNT(*) INTO v_existing_school FROM schools WHERE LOWER(code) = LOWER(p_code);
    IF v_existing_school > 0 THEN
        RAISE EXCEPTION 'School code already exists';
    END IF;

    INSERT INTO schools(
        id, school_id, is_deleted, created_at, created_by,
        code, name, admin_name, contact_phone, address_line, is_active, is_soft_deleted,
        license_start_date, license_end_date, max_students, max_staff)
    VALUES (
        uuid_generate_v4(),
        NULL,
        FALSE,
        NOW(),
        p_created_by,
        p_code,
        p_name,
        NULLIF(p_admin_name, ''),
        NULLIF(p_contact_phone, ''),
        NULLIF(p_address_line, ''),
        TRUE,
        FALSE,
        NOW(),
        NOW() + INTERVAL '1 year',
        0,
        0
    )
    RETURNING id INTO v_school_id;

    -- Ensure a SchoolAdmin role exists at platform level
    SELECT id INTO v_admin_role_id
    FROM roles
    WHERE school_id IS NULL
      AND name = 'SchoolAdmin'
      AND is_deleted = FALSE
    LIMIT 1;

    IF v_admin_role_id IS NULL THEN
        INSERT INTO roles(
            id, school_id, is_deleted, created_at, created_by,
            name, description)
        VALUES (
            uuid_generate_v4(),
            NULL,
            FALSE,
            NOW(),
            p_created_by,
            'SchoolAdmin',
            'Default school administrator role')
        RETURNING id INTO v_admin_role_id;
    END IF;

    SELECT COUNT(*) INTO v_existing_user
    FROM users
    WHERE LOWER(email) = LOWER(p_admin_email)
      AND school_id = v_school_id
      AND is_deleted = FALSE;

    IF v_existing_user > 0 THEN
        RAISE EXCEPTION 'Admin email already exists for this school';
    END IF;

    INSERT INTO users(
        id, school_id, is_deleted, created_at, created_by,
        email, password_hash, full_name, role_id,
        is_locked, failed_login_attempts, must_change_password, is_soft_deleted_user)
    VALUES (
        uuid_generate_v4(),
        v_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_admin_email,
        p_admin_password_hash,
        COALESCE(NULLIF(p_admin_name, ''), p_name || ' Admin'),
        v_admin_role_id,
        FALSE,
        0,
        TRUE,
        FALSE
    );
END;
$$;

CREATE OR REPLACE PROCEDURE sp_school_activate(p_school_id UUID, p_user_id UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE schools
    SET is_active = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_school_id
      AND is_deleted = FALSE;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_school_deactivate(p_school_id UUID, p_user_id UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE schools
    SET is_active = FALSE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_school_id
      AND is_deleted = FALSE;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_school_soft_delete(p_school_id UUID, p_user_id UUID)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE schools
    SET is_soft_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_school_id
      AND is_deleted = FALSE;
END;
$$;

-- Assign or update a subscription plan for a school, enforcing no downgrade below usage.

CREATE OR REPLACE PROCEDURE sp_subscription_assign(
    p_school_id UUID,
    p_subscription_plan_id UUID,
    p_start_date TIMESTAMP,
    p_end_date TIMESTAMP,
    p_auto_renew BOOLEAN,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_plan RECORD;
    v_students INT;
    v_staff INT;
BEGIN
    SELECT * INTO v_plan
    FROM subscription_plans
    WHERE id = p_subscription_plan_id
      AND is_deleted = FALSE
      AND is_active = TRUE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Subscription plan not found or inactive';
    END IF;

    SELECT COUNT(*) INTO v_students
    FROM students
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    SELECT COUNT(*) INTO v_staff
    FROM staff
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    IF v_students > v_plan.max_students THEN
        RAISE EXCEPTION 'Cannot assign plan with student limit % below current usage %',
            v_plan.max_students, v_students;
    END IF;

    IF v_staff > v_plan.max_staff THEN
        RAISE EXCEPTION 'Cannot assign plan with staff limit % below current usage %',
            v_plan.max_staff, v_staff;
    END IF;

    UPDATE school_subscriptions
    SET is_active = FALSE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE school_id_fk = p_school_id
      AND is_active = TRUE;

    INSERT INTO school_subscriptions(
        id, school_id, is_deleted, created_at, created_by,
        school_id_fk, subscription_plan_id, start_date, end_date,
        is_active, auto_renew)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_school_id,
        p_subscription_plan_id,
        p_start_date,
        p_end_date,
        TRUE,
        p_auto_renew
    );

    UPDATE schools
    SET license_start_date = p_start_date,
        license_end_date = p_end_date,
        max_students = v_plan.max_students,
        max_staff = v_plan.max_staff,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_school_id;
END;
$$;

