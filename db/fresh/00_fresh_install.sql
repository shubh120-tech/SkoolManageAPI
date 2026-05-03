\echo 'Running fresh DB install scripts...'

-- NOTE:
-- This script is intended to be executed with `psql` so `\i` works.
-- This folder is self-contained for cloud DB migrations (no ../ references).

\i schema.sql
\i functions_and_procs_auth.sql
\i functions_reporting.sql

\i stored_procedures_school.sql
\i stored_procedures_academic_sessions.sql
\i stored_procedures_academic.sql
\i stored_procedures_students.sql
\i stored_procedures_staff.sql
\i stored_procedures_fees.sql
\i stored_procedures_attendance.sql
\i stored_procedures_payroll.sql
\i stored_procedures_timetable.sql
\i stored_procedures_timetable_delete.sql
\i stored_procedures_communication.sql

\i seed_platform_permissions.sql

-- Apply migrations last (kept for compatibility with older DBs / incremental changes).
\i migrations/001_fix_sp_student_admit_signature.sql
\i migrations/002_add_fn_staff_create.sql
\i migrations/003_staff_address_and_date_of_joining.sql
\i migrations/004_sp_staff_update_timestamp_params.sql
\i migrations/005_fn_class_teacher_get_by_teacher.sql
\i migrations/006_class_teacher_one_per_teacher_per_session.sql
\i migrations/007_add_fn_fee_head_create.sql
\i migrations/008_class_fee_structure_optional_and_discount.sql
\i migrations/009_allow_teacher_multiple_classes.sql
\i migrations/010_teacher_role_permissions.sql
\i migrations/011_user_permissions.sql
\i migrations/012_leave_half_day.sql
\i migrations/012_fix_login_v_school_record.sql

\echo 'Fresh DB install scripts completed.'

