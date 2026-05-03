## School Management SaaS Backend (New Project)

This is the new multi-tenant School Management SaaS backend you asked for, located under the `src` folder with a fresh .NET 9 solution.

### Solution Layout

- `SchoolManagement.sln`
- `SchoolManagement.Domain` – entities and core domain types.
- `SchoolManagement.Application` – interfaces, DTOs, use-case services, `ApiResponse` wrapper, error codes.
- `SchoolManagement.Infrastructure` – PostgreSQL access (Dapper, Npgsql), JWT, BCrypt, audit logging, current user/tenant context, auth service.
- `SchoolManagement.API` – ASP.NET Core Web API, controllers, middleware, authentication/authorization wiring.
- `db` – PostgreSQL schema, auth functions and stored procedures.
- `docker` – `Dockerfile` and `docker-compose.yml`.

### Database Setup

The `db` folder contains:

- `schema.sql` – all platform and tenant tables with:
  - `id`, `school_id`, `is_deleted`, `created_at`, `created_by`, `updated_at`, `updated_by`.
  - Multi-tenant constraints (`school_id` on tenant tables; nullable on platform tables).
  - Required unique constraints:
    - `UNIQUE (school_id, admission_no)` via `students`.
    - `UNIQUE (school_id, email)` on `students` and `staff`.
    - `UNIQUE (school_id, class_id, section_id, attendance_date)` via `attendance_days`.
    - `UNIQUE (school_id, receipt_number)` via `fee_payments`.
    - `UNIQUE (school_id, staff_id, month, year)` via `payroll_records`.
- `functions_and_procs_auth.sql` – functions/procedures used by the API:
  - `fn_auth_get_user_for_login`
  - `sp_auth_register_failed_login`
  - `sp_auth_register_successful_login`
  - `sp_auth_create_refresh_token`
  - `fn_auth_rotate_refresh_token`
  - `sp_auth_revoke_refresh_token`
  - `fn_auth_get_user_password_hash`
  - `sp_auth_change_password`
  - `sp_auth_generate_reset_password_token`
  - `sp_auth_reset_password`
  - `sp_audit_log_create`
  - `fn_school_check_status`
  - `fn_school_check_license`

All of these respect the multi-tenant model (validate `school_id`, soft deletes, license status, and lockout rules).

### Running with Docker

From `C:\Users\shubh\SchoolManagement\src`:

```bash
cd docker
docker-compose up --build
```

This will:

- Start PostgreSQL with the schema and auth functions/procedures pre-applied.
- Build and run the API container on `http://localhost:5000`.

The API uses the connection string from environment variables in `docker-compose.yml`, pointing to the `db` service.

### Running Locally (without Docker)

1. Ensure PostgreSQL is running locally and create a database `school_management`.
2. Apply the SQL:

```bash
psql -h localhost -U school_user -d school_management -f db/schema.sql
psql -h localhost -U school_user -d school_management -f db/functions_and_procs_auth.sql
```

3. Update `SchoolManagement.API/appsettings.json` with your local `DefaultConnection`.
4. Run the API:

```bash
cd src
dotnet run --project SchoolManagement.API
```

The API will be available at `https://localhost:5001` (or `http://localhost:5000` depending on your launch settings).

