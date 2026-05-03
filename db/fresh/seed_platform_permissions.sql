-- Seed platform-level permissions and assign them to SchoolAdmin role.
-- Run once after schema (and after sp_school_register has created SchoolAdmin if needed).
-- Uses a fixed system user UUID for created_by (no real user).

DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_school_admin_role_id UUID;
    v_perm_id UUID;
    v_perm_name TEXT;
    v_permissions TEXT[] := ARRAY[
        'Student.View', 'Student.View.All',
        'Student.Admit', 'Student.Admit.All',
        'Student.Update', 'Student.Update.All',
        'Student.Promote', 'Student.Promote.All',
        'Student.Delete', 'Student.Manage',
        'Academic.Classes.View', 'Academic.Classes.Manage', 'Academic.ClassesSections.ManageAll',
        'Academic.Sections.View', 'Academic.Sections.Manage',
        'Academic.Sessions.View', 'Academic.Sessions.Manage',
        'Staff.View', 'Staff.Manage',
        'Fees.View', 'Fees.View.All',
        'Fees.Manage', 'Fees.Manage.All',
        'Fees.Payments', 'Fees.Payments.All',
        'Attendance.Student.Manage',
        'Attendance.Staff.Manage',
        -- Legacy permission (kept for backward compatibility; backend now uses split permissions)
        'Attendance.Manage',
        'Leave.Apply', 'Leave.ViewOwn', 'Leave.View.All', 'Leave.Approve', 'Leave.Approve.All',
        'Timetable.Manage', 'Timetable.View',
        'Payroll.View', 'Payroll.View.All',
        'Payroll.Generate', 'Payroll.Generate.All',
        'Payroll.Pay', 'Payroll.Pay.All',
        'Payroll.History.View', 'Payroll.History.View.All',
        'Communication.Announcements.View', 'Communication.Announcements.Manage',
        'Reporting.Students.View', 'Reporting.Students.Overview',
        'Reporting.Staff.Payroll', 'Reporting.Timetable.View'
    ];
BEGIN
    -- Get SchoolAdmin role (platform-level)
    SELECT id INTO v_school_admin_role_id
    FROM roles
    WHERE school_id IS NULL AND name = 'SchoolAdmin' AND is_deleted = FALSE
    LIMIT 1;

    IF v_school_admin_role_id IS NULL THEN
        RAISE NOTICE 'SchoolAdmin role not found. Run sp_school_register first or create the role.';
        RETURN;
    END IF;

    -- Ensure each permission exists (platform-level) and assign to SchoolAdmin
    FOREACH v_perm_name IN ARRAY v_permissions
    LOOP
        IF NOT EXISTS (SELECT 1 FROM permissions WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE) THEN
            INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
            VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, v_perm_name, 'Platform permission: ' || v_perm_name);
        END IF;

        SELECT id INTO v_perm_id
        FROM permissions
        WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE
        LIMIT 1;

        IF v_perm_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM role_permissions WHERE role_id = v_school_admin_role_id AND permission_id = v_perm_id AND is_deleted = FALSE
        ) THEN
            INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
            VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, v_school_admin_role_id, v_perm_id);
        END IF;
    END LOOP;

    RAISE NOTICE 'Platform permissions seeded and assigned to SchoolAdmin.';
END $$;
