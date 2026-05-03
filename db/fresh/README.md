## Fresh DB scripts

This folder contains a **self-contained fresh-install** copy of the database SQL for this project (tables, functions, procedures, and migrations).

### Recommended execution order (PostgreSQL)

Option A (recommended): run the master script:

1. `00_fresh_install.sql`

Option B: run files manually in this order:

1. `schema.sql`
2. `functions_and_procs_auth.sql`
3. `functions_reporting.sql`
4. Stored procedures:
   - `stored_procedures_school.sql`
   - `stored_procedures_academic_sessions.sql`
   - `stored_procedures_academic.sql`
   - `stored_procedures_students.sql`
   - `stored_procedures_staff.sql`
   - `stored_procedures_fees.sql`
   - `stored_procedures_attendance.sql`
   - `stored_procedures_payroll.sql`
   - `stored_procedures_timetable.sql`
   - `stored_procedures_timetable_delete.sql`
   - `stored_procedures_communication.sql`
5. Seeds:
   - `seed_platform_permissions.sql`
6. Migrations:
   - `migrations/*.sql` (run in filename order)

### Notes

- These are **copies** of the current scripts from `src/db/` so you can upload only `fresh/` to your cloud migration environment and run it there.
- If you change SQL in `src/db/`, remember to re-copy into this folder (or regenerate it).

