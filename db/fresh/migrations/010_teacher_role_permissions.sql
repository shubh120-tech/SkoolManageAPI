-- Grant Teacher role the read-only permissions needed for teacher dashboard and My Classes.
-- Also add Timetable.View so GET /reporting/timetable can be used by teachers; assign to SchoolAdmin too.

DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_teacher_role_id UUID;
    v_school_admin_role_id UUID;
    v_perm_id UUID;
    v_perm_name TEXT;
    -- Default Teacher permissions:
    -- - Academic.Classes.View / Academic.Sections.View / Academic.Sessions.View  (view academic structure)
    -- - Student.View (own-school students; backend already restricts by school + teacher assignments)
    -- - Reporting.Students.View (global search, but API should still filter to teacher's classes)
    -- - Attendance.Student.Manage (mark/view student attendance for assigned classes only)
    -- - Timetable.View (view timetable for assigned classes)
    -- - Reporting.Staff.Payroll (view own payroll / payment history)
    v_teacher_permissions TEXT[] := ARRAY[
        'Academic.Classes.View',
        'Academic.Sections.View',
        'Academic.Sessions.View',
        'Student.View',
        'Reporting.Students.View',
        'Attendance.Student.Manage',
        'Leave.Apply',
        'Leave.ViewOwn',
        'Timetable.View',
        'Reporting.Staff.Payroll',
	'Reporting.Staff.View' 
    ];
BEGIN
    -- Ensure Timetable.View permission exists (platform-level)
    IF NOT EXISTS (SELECT 1 FROM permissions WHERE school_id IS NULL AND name = 'Timetable.View' AND is_deleted = FALSE) THEN
        INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
        VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, 'Timetable.View', 'Platform permission: view timetable');
    END IF;

    -- Get or create Teacher role (platform-level)
    SELECT id INTO v_teacher_role_id
    FROM roles
    WHERE school_id IS NULL AND name = 'Teacher' AND is_deleted = FALSE
    LIMIT 1;

    IF v_teacher_role_id IS NULL THEN
        INSERT INTO roles (id, school_id, is_deleted, created_at, created_by, name, description)
        VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, 'Teacher', 'Teaching staff role')
        RETURNING id INTO v_teacher_role_id;
    END IF;

    -- Assign each permission to Teacher role
    FOREACH v_perm_name IN ARRAY v_teacher_permissions
    LOOP
        SELECT id INTO v_perm_id
        FROM permissions
        WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE
        LIMIT 1;

        IF v_perm_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM role_permissions WHERE role_id = v_teacher_role_id AND permission_id = v_perm_id AND is_deleted = FALSE
        ) THEN
            INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
            VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, v_teacher_role_id, v_perm_id);
        END IF;
    END LOOP;

    -- Assign Timetable.View to SchoolAdmin so they can still view timetable (GET uses Timetable.View)
    SELECT id INTO v_school_admin_role_id
    FROM roles
    WHERE school_id IS NULL AND name = 'SchoolAdmin' AND is_deleted = FALSE
    LIMIT 1;

    IF v_school_admin_role_id IS NOT NULL THEN
        SELECT id INTO v_perm_id
        FROM permissions
        WHERE school_id IS NULL AND name = 'Timetable.View' AND is_deleted = FALSE
        LIMIT 1;

        IF v_perm_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM role_permissions WHERE role_id = v_school_admin_role_id AND permission_id = v_perm_id AND is_deleted = FALSE
        ) THEN
            INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
            VALUES (uuid_generate_v4(), NULL, FALSE, NOW(), v_system_user, v_school_admin_role_id, v_perm_id);
        END IF;
    END IF;

    RAISE NOTICE 'Teacher role permissions and Timetable.View (Teacher + SchoolAdmin) applied.';
END $$;
