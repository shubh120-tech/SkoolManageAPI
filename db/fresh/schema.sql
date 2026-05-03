CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- PLATFORM TABLES

CREATE TABLE schools (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    admin_name VARCHAR(200) NULL,
    contact_phone VARCHAR(50) NULL,
    address_line VARCHAR(300) NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_soft_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    license_start_date TIMESTAMP NULL,
    license_end_date TIMESTAMP NULL,
    max_students INT NOT NULL DEFAULT 0,
    max_staff INT NOT NULL DEFAULT 0
);

CREATE TABLE subscription_plans (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL UNIQUE,
    price_per_month NUMERIC(12,2) NOT NULL,
    max_students INT NOT NULL,
    max_staff INT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE school_subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    school_id_fk UUID NOT NULL REFERENCES schools(id),
    subscription_plan_id UUID NOT NULL REFERENCES subscription_plans(id),
    start_date TIMESTAMP NOT NULL,
    end_date TIMESTAMP NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    auto_renew BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE roles (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL,
    description TEXT NULL,
    CONSTRAINT uq_roles_name UNIQUE (school_id, name)
);

CREATE TABLE permissions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(150) NOT NULL,
    description TEXT NULL,
    CONSTRAINT uq_permissions_name UNIQUE (school_id, name)
);

CREATE TABLE role_permissions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    role_id UUID NOT NULL REFERENCES roles(id),
    permission_id UUID NOT NULL REFERENCES permissions(id),
    CONSTRAINT uq_role_permissions UNIQUE (role_id, permission_id)
);

CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    email VARCHAR(200) NOT NULL,
    password_hash TEXT NOT NULL,
    full_name VARCHAR(200) NOT NULL,
    role_id UUID NOT NULL REFERENCES roles(id),
    is_locked BOOLEAN NOT NULL DEFAULT FALSE,
    failed_login_attempts INT NOT NULL DEFAULT 0,
    must_change_password BOOLEAN NOT NULL DEFAULT FALSE,
    is_soft_deleted_user BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT uq_users_email UNIQUE (school_id, email)
);

CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    user_id UUID NOT NULL REFERENCES users(id),
    token_hash TEXT NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
    replaced_by_token_hash TEXT NULL
);

CREATE TABLE audit_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    user_id UUID NULL REFERENCES users(id),
    action VARCHAR(200) NOT NULL,
    entity_name VARCHAR(200) NOT NULL,
    entity_id UUID NULL,
    details_json TEXT NULL,
    ip_address VARCHAR(100) NULL
);

-- TENANT TABLES (academic, students, staff, attendance, fees, payroll, timetable, communication)

CREATE TABLE academic_sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL,
    start_date TIMESTAMP NOT NULL,
    end_date TIMESTAMP NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE classes (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL,
    capacity INT NOT NULL,
    academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    CONSTRAINT uq_classes_name_per_session UNIQUE (school_id, academic_session_id, name)
);

CREATE TABLE sections (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    class_id UUID NOT NULL REFERENCES classes(id),
    name VARCHAR(50) NOT NULL,
    capacity INT NOT NULL,
    academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    CONSTRAINT uq_sections_class_name_per_session UNIQUE (school_id, class_id, academic_session_id, name)
);

CREATE TABLE subjects (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL,
    code VARCHAR(50) NULL,
    CONSTRAINT uq_subjects_name UNIQUE (school_id, name)
);

CREATE TABLE staff (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_code VARCHAR(50) NOT NULL,
    full_name VARCHAR(200) NOT NULL,
    email VARCHAR(200) NULL,
    date_of_birth DATE NULL,
    mobile_no VARCHAR(20) NULL,
    is_teaching BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT uq_staff_email UNIQUE (school_id, email)
);

CREATE TABLE class_teachers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    class_id UUID NOT NULL REFERENCES classes(id),
    section_id UUID NOT NULL REFERENCES sections(id),
    teacher_id UUID NOT NULL REFERENCES staff(id),
    academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    CONSTRAINT uq_class_teachers UNIQUE (school_id, class_id, section_id, academic_session_id)
);

CREATE TABLE staff_subjects (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_id UUID NOT NULL REFERENCES staff(id),
    subject_id UUID NOT NULL REFERENCES subjects(id),

    CONSTRAINT uq_staff_subject UNIQUE (school_id, staff_id, subject_id)
);

 

CREATE TABLE students (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    admission_no VARCHAR(50) NOT NULL,
    first_name VARCHAR(100) NOT NULL,
    middle_name VARCHAR(100) NULL,
    last_name VARCHAR(100) NOT NULL,
    full_name VARCHAR(200) NOT NULL,
    class_id UUID NOT NULL REFERENCES classes(id),
    section_id UUID NOT NULL REFERENCES sections(id),
    academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    date_of_birth DATE NOT NULL,
    email VARCHAR(200) NULL,
    parent_mobile_no VARCHAR(20) NULL,
    father_name VARCHAR(200) NULL,
    mother_name VARCHAR(200) NULL,
    address_line VARCHAR(300) NULL,
    blood_group VARCHAR(10) NULL,
    aadhar_no VARCHAR(20) NULL,
    roll_no INT NULL,
    CONSTRAINT uq_students_admission_no UNIQUE (school_id, admission_no)
);

CREATE UNIQUE INDEX uq_students_school_aadhar_norm ON students (school_id, (regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')))
WHERE is_deleted = FALSE AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> '';

CREATE TABLE student_history (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    student_id UUID NOT NULL REFERENCES students(id),
    from_class_id UUID NOT NULL REFERENCES classes(id),
    from_section_id UUID NOT NULL REFERENCES sections(id),
    to_class_id UUID NOT NULL REFERENCES classes(id),
    to_section_id UUID NOT NULL REFERENCES sections(id),
    from_academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    to_academic_session_id UUID NOT NULL REFERENCES academic_sessions(id),
    action VARCHAR(50) NOT NULL
);

CREATE TABLE leave_requests (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_id UUID NOT NULL REFERENCES staff(id),
    from_date DATE NOT NULL,
    to_date DATE NOT NULL,
    reason TEXT NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'Pending'
);

CREATE TABLE attendance_days (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    class_id UUID NOT NULL REFERENCES classes(id),
    section_id UUID NOT NULL REFERENCES sections(id),
    attendance_date DATE NOT NULL,
    CONSTRAINT uq_attendance_day UNIQUE (school_id, class_id, section_id, attendance_date)
);

CREATE TABLE attendance_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    attendance_day_id UUID NOT NULL REFERENCES attendance_days(id),
    student_id UUID NOT NULL REFERENCES students(id),
    is_present BOOLEAN NOT NULL,
    CONSTRAINT uq_attendance_records UNIQUE (school_id, attendance_day_id, student_id)
);

CREATE TABLE staff_attendance_days (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    attendance_date DATE NOT NULL,
    CONSTRAINT uq_staff_attendance_day UNIQUE (school_id, attendance_date)
);

CREATE TABLE staff_attendance_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    attendance_day_id UUID NOT NULL REFERENCES staff_attendance_days(id),
    staff_id UUID NOT NULL REFERENCES staff(id),
    is_present BOOLEAN NOT NULL,
    CONSTRAINT uq_staff_attendance_records UNIQUE (school_id, attendance_day_id, staff_id)
);

CREATE TABLE fee_heads (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    name VARCHAR(100) NOT NULL,
    is_recurring BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_fee_heads_name UNIQUE (school_id, name)
);

CREATE TABLE class_fee_structures (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    class_id UUID NOT NULL REFERENCES classes(id),
    fee_head_id UUID NOT NULL REFERENCES fee_heads(id),
    amount NUMERIC(12,2) NOT NULL,
    is_optional BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE student_fee_assignments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    student_id UUID NOT NULL REFERENCES students(id),
    fee_head_id UUID NOT NULL REFERENCES fee_heads(id),
    amount NUMERIC(12,2) NOT NULL,
    due_date DATE NOT NULL,
    discount_amount NUMERIC(12,2) NULL,
    discount_reason VARCHAR(200) NULL,
    late_fine_amount NUMERIC(12,2) NULL,
    is_paid BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE fee_payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    student_id UUID NOT NULL REFERENCES students(id),
    receipt_number VARCHAR(50) NOT NULL,
    amount_paid NUMERIC(12,2) NOT NULL,
    payment_date TIMESTAMP NOT NULL,
    CONSTRAINT uq_fee_payments_receipt_number UNIQUE (school_id, receipt_number)
);

CREATE TABLE salary_structures (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_id UUID NOT NULL REFERENCES staff(id),
    basic NUMERIC(12,2) NOT NULL,
    allowances NUMERIC(12,2) NOT NULL,
    deductions NUMERIC(12,2) NOT NULL
);

CREATE TABLE staff_bank_details (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_id UUID NOT NULL REFERENCES staff(id),
    account_no VARCHAR(50) NOT NULL,
    ifsc_code VARCHAR(20) NOT NULL,
    bank_name VARCHAR(200) NOT NULL,
    CONSTRAINT uq_staff_bank_details UNIQUE (school_id, staff_id)
);

CREATE TABLE payroll_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    staff_id UUID NOT NULL REFERENCES staff(id),
    month INT NOT NULL,
    year INT NOT NULL,
    gross_amount NUMERIC(12,2) NOT NULL,
    net_amount NUMERIC(12,2) NOT NULL,
    generated_on TIMESTAMP NOT NULL,
    -- Carry-forward and payment tracking
    previous_pending NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_due NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_paid NUMERIC(12,2) NOT NULL DEFAULT 0,
    pending_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    CONSTRAINT uq_payroll_records UNIQUE (school_id, staff_id, month, year)
);

CREATE TABLE staff_payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    payroll_record_id UUID NOT NULL REFERENCES payroll_records(id),
    staff_id UUID NOT NULL REFERENCES staff(id),
    amount_paid NUMERIC(12,2) NOT NULL,
    payment_date TIMESTAMP NOT NULL,
    payment_mode VARCHAR(50) NOT NULL,
    reference_no VARCHAR(100),
    remarks TEXT
);

CREATE TABLE leave_requests (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    from_date DATE NOT NULL,
    to_date DATE NOT NULL,
    reason TEXT NOT NULL,
    leave_type VARCHAR(50) NOT NULL DEFAULT 'General',
    status VARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending | Approved | Rejected | Cancelled
    approver_id UUID NULL REFERENCES users(id),
    decision_date TIMESTAMP NULL,
    admin_remarks TEXT NULL
);

CREATE TABLE timetables (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    class_id UUID NOT NULL REFERENCES classes(id),
    section_id UUID NOT NULL REFERENCES sections(id),
    subject_id UUID NOT NULL REFERENCES subjects(id),
    teacher_id UUID NOT NULL REFERENCES staff(id),
    day_of_week INT NOT NULL,
    start_time TIME NOT NULL,
    end_time TIME NOT NULL,
    CONSTRAINT uq_timetables_class_slot UNIQUE (school_id, class_id, section_id, day_of_week, start_time)
);

CREATE TABLE announcements (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    title VARCHAR(200) NOT NULL,
    message TEXT NOT NULL,
    valid_from TIMESTAMP NOT NULL,
    valid_to TIMESTAMP NOT NULL,
    for_students BOOLEAN NOT NULL DEFAULT TRUE,
    for_staff BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    school_id UUID NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,

    user_id UUID NOT NULL REFERENCES users(id),
    title VARCHAR(200) NOT NULL,
    message TEXT NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    link_url TEXT NULL
);

