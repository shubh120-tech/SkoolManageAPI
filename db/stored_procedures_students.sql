-- STUDENT PROCEDURES (ADMIT / PROMOTE) WITH SUBSCRIPTION ENFORCEMENT

CREATE OR REPLACE PROCEDURE sp_student_admit(
    p_school_id UUID,
    p_first_name TEXT,
    p_middle_name TEXT,
    p_last_name TEXT,
    p_class_id UUID,
    p_section_id UUID,
    p_date_of_birth TIMESTAMPTZ,
    p_email TEXT,
    p_parent_mobile_no TEXT,
    p_father_name TEXT,
    p_mother_name TEXT,
    p_address_line TEXT,
    p_blood_group TEXT,
    p_aadhar_no TEXT,
    p_udise_no TEXT,
    p_father_aadhar_no TEXT,
    p_mother_aadhar_no TEXT,
    p_father_occupation TEXT,
    p_mother_occupation TEXT,
    p_pen_no TEXT,
    p_bank_name TEXT,
    p_bank_account_no TEXT,
    p_bank_ifsc TEXT,
    p_bank_branch TEXT,
    p_created_by UUID,
    OUT o_student_id UUID,
    OUT o_admission_no TEXT,
    OUT o_full_name TEXT,
    OUT o_academic_session_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_student_count INT;
    v_school RECORD;
    v_class RECORD;
    v_section RECORD;
    v_session RECORD;
    v_existing INT;
    v_adm_no TEXT;
    v_full_name TEXT;
    v_next_roll_no INT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_student_admit_' || p_school_id::text));

    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_school.is_active OR v_school.is_soft_deleted THEN
        RAISE EXCEPTION 'School invalid or inactive';
    END IF;

    IF v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW() THEN
        RAISE EXCEPTION 'School license expired';
    END IF;

    SELECT COUNT(*) INTO v_student_count
    FROM students
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    IF v_school.max_students > 0 AND v_student_count >= v_school.max_students THEN
        RAISE EXCEPTION 'Student limit reached for subscription';
    END IF;

    SELECT * INTO v_class FROM classes WHERE id = p_class_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found';
    END IF;

    SELECT * INTO v_section FROM sections WHERE id = p_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;

    IF v_section.class_id <> p_class_id THEN
        RAISE EXCEPTION 'Section does not belong to the selected class';
    END IF;

    IF v_section.academic_session_id <> v_class.academic_session_id THEN
        RAISE EXCEPTION 'Class and section must belong to the same academic session';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions
    WHERE id = v_class.academic_session_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Academic session not found for selected class';
    END IF;
    o_academic_session_id := v_session.id;


    v_full_name := trim(both ' ' from coalesce(p_first_name, '') || ' ' || coalesce(p_middle_name, '') || ' ' || coalesce(p_last_name, ''));

    SELECT format('ADM-%s', LPAD((COALESCE(MAX(CAST(NULLIF(TRIM(substring(admission_no from '[0-9]+$')), '') AS INT)), 0) + 1)::text, 6, '0'))
    INTO v_adm_no
    FROM students
    WHERE school_id = p_school_id;

    -- Next roll number within class + section + academic session
    SELECT COALESCE(MAX(roll_no), 0) + 1
    INTO v_next_roll_no
    FROM students
    WHERE school_id = p_school_id
      AND academic_session_id = v_session.id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND is_deleted = FALSE;

    IF p_aadhar_no IS NOT NULL AND trim(p_aadhar_no) <> '' THEN
        SELECT COUNT(*) INTO v_existing
        FROM students
        WHERE school_id = p_school_id
          AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
              = regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
          AND regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> ''
          AND is_deleted = FALSE;
        IF v_existing > 0 THEN
            RAISE EXCEPTION 'Duplicate student Aadhaar number';
        END IF;
    END IF;

    INSERT INTO students(
        id, school_id, is_deleted, created_at, created_by,
        admission_no, first_name, middle_name, last_name, full_name,
        class_id, section_id,
        academic_session_id, date_of_birth, email,
        parent_mobile_no, father_name, mother_name, address_line, blood_group, aadhar_no, roll_no,
        udise_no, father_aadhar_no, mother_aadhar_no, father_occupation, mother_occupation, pen_no,
        bank_name, bank_account_no, bank_ifsc, bank_branch)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        v_adm_no,
        p_first_name,
        p_middle_name,
        p_last_name,
        v_full_name,
        p_class_id,
        p_section_id,
        v_session.id,
        p_date_of_birth::DATE,
        p_email,
        p_parent_mobile_no,
        p_father_name,
        p_mother_name,
        p_address_line,
        p_blood_group,
        p_aadhar_no,
        v_next_roll_no,
        p_udise_no,
        p_father_aadhar_no,
        p_mother_aadhar_no,
        p_father_occupation,
        p_mother_occupation,
        p_pen_no,
        p_bank_name,
        p_bank_account_no,
        p_bank_ifsc,
        p_bank_branch
    )
    RETURNING id, admission_no, full_name
    INTO o_student_id, o_admission_no, o_full_name;
END;
$$;

-- Function with same extended columns as inline admit (StudentService). Session comes from class/section.
-- Call from app: SELECT * FROM fn_student_admit(...) returns one row (o_student_id, o_admission_no, o_full_name, o_academic_session_id).
CREATE OR REPLACE FUNCTION fn_student_admit(
    p_school_id UUID,
    p_first_name TEXT,
    p_middle_name TEXT,
    p_last_name TEXT,
    p_class_id UUID,
    p_section_id UUID,
    p_date_of_birth TIMESTAMPTZ,
    p_email TEXT,
    p_parent_mobile_no TEXT,
    p_father_name TEXT,
    p_mother_name TEXT,
    p_address_line TEXT,
    p_blood_group TEXT,
    p_aadhar_no TEXT,
    p_udise_no TEXT,
    p_father_aadhar_no TEXT,
    p_mother_aadhar_no TEXT,
    p_father_occupation TEXT,
    p_mother_occupation TEXT,
    p_pen_no TEXT,
    p_bank_name TEXT,
    p_bank_account_no TEXT,
    p_bank_ifsc TEXT,
    p_bank_branch TEXT,
    p_created_by UUID)
RETURNS TABLE(o_student_id UUID, o_admission_no TEXT, o_full_name TEXT, o_academic_session_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_student_count INT;
    v_school RECORD;
    v_class RECORD;
    v_section RECORD;
    v_session RECORD;
    v_existing INT;
    v_adm_no TEXT;
    v_full_name TEXT;
    v_next_roll_no INT;
    v_id UUID;
    v_adm TEXT;
    v_name TEXT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('fn_student_admit_' || p_school_id::text));

    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_school.is_active OR v_school.is_soft_deleted THEN
        RAISE EXCEPTION 'School invalid or inactive';
    END IF;

    IF v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW() THEN
        RAISE EXCEPTION 'School license expired';
    END IF;

    SELECT COUNT(*) INTO v_student_count
    FROM students
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    IF v_school.max_students > 0 AND v_student_count >= v_school.max_students THEN
        RAISE EXCEPTION 'Student limit reached for subscription';
    END IF;

    SELECT * INTO v_class FROM classes WHERE id = p_class_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found';
    END IF;

    SELECT * INTO v_section FROM sections WHERE id = p_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;
    IF v_section.class_id <> p_class_id THEN
        RAISE EXCEPTION 'Section does not belong to the selected class';
    END IF;
    IF v_section.academic_session_id <> v_class.academic_session_id THEN
        RAISE EXCEPTION 'Class and section must belong to the same academic session';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions
    WHERE id = v_class.academic_session_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Academic session not found for selected class';
    END IF;

    IF p_aadhar_no IS NOT NULL AND trim(p_aadhar_no) <> '' THEN
        SELECT COUNT(*) INTO v_existing
        FROM students
        WHERE school_id = p_school_id
          AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
              = regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
          AND regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> ''
          AND is_deleted = FALSE;
        IF v_existing > 0 THEN
            RAISE EXCEPTION 'Duplicate student Aadhaar number';
        END IF;
    END IF;

    v_full_name := trim(both ' ' from coalesce(p_first_name, '') || ' ' || coalesce(p_middle_name, '') || ' ' || coalesce(p_last_name, ''));

    SELECT format('ADM-%s', LPAD((COALESCE(MAX(CAST(NULLIF(TRIM(substring(admission_no from '[0-9]+$')), '') AS INT)), 0) + 1)::text, 6, '0'))
    INTO v_adm_no
    FROM students
    WHERE school_id = p_school_id;

    SELECT COALESCE(MAX(roll_no), 0) + 1
    INTO v_next_roll_no
    FROM students
    WHERE school_id = p_school_id
      AND academic_session_id = v_session.id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND is_deleted = FALSE;

    INSERT INTO students(
        id, school_id, is_deleted, created_at, created_by,
        admission_no, first_name, middle_name, last_name, full_name,
        class_id, section_id,
        academic_session_id, date_of_birth, email,
        parent_mobile_no, father_name, mother_name, address_line, blood_group, aadhar_no, roll_no,
        udise_no, father_aadhar_no, mother_aadhar_no, father_occupation, mother_occupation, pen_no,
        bank_name, bank_account_no, bank_ifsc, bank_branch)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        v_adm_no,
        p_first_name,
        p_middle_name,
        p_last_name,
        v_full_name,
        p_class_id,
        p_section_id,
        v_session.id,
        p_date_of_birth::DATE,
        p_email,
        p_parent_mobile_no,
        p_father_name,
        p_mother_name,
        p_address_line,
        p_blood_group,
        p_aadhar_no,
        v_next_roll_no,
        p_udise_no,
        p_father_aadhar_no,
        p_mother_aadhar_no,
        p_father_occupation,
        p_mother_occupation,
        p_pen_no,
        p_bank_name,
        p_bank_account_no,
        p_bank_ifsc,
        p_bank_branch
    )
    RETURNING id, admission_no, full_name
    INTO v_id, v_adm, v_name;

    o_student_id := v_id;
    o_admission_no := v_adm;
    o_full_name := v_name;
    o_academic_session_id := v_session.id;
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_student_promote(
    p_school_id UUID,
    p_student_id UUID,
    p_to_class_id UUID,
    p_to_section_id UUID,
    p_to_academic_session_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_student RECORD;
    v_to_class RECORD;
    v_to_section RECORD;
    v_to_session RECORD;
    v_next_roll_no INT;
BEGIN
    SELECT * INTO v_student
    FROM students
    WHERE id = p_student_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Student not found';
    END IF;

    SELECT * INTO v_to_class FROM classes WHERE id = p_to_class_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Destination class not found';
    END IF;

    SELECT * INTO v_to_section FROM sections WHERE id = p_to_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Destination section not found';
    END IF;

    SELECT * INTO v_to_session
    FROM academic_sessions
    WHERE id = p_to_academic_session_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Destination academic session not found';
    END IF;
    -- Sessions may be inactive (e.g. next-year setup while current session runs); do not require is_active.

    IF v_to_class.academic_session_id IS DISTINCT FROM p_to_academic_session_id THEN
        RAISE EXCEPTION 'Destination class does not belong to the selected academic session';
    END IF;
    IF v_to_section.class_id IS DISTINCT FROM p_to_class_id
       OR v_to_section.academic_session_id IS DISTINCT FROM p_to_academic_session_id THEN
        RAISE EXCEPTION 'Destination section does not belong to the selected class and session';
    END IF;

    INSERT INTO student_history(
        id, school_id, is_deleted, created_at, created_by,
        student_id, from_class_id, from_section_id, to_class_id,
        to_section_id, from_academic_session_id, to_academic_session_id,
        action)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        v_student.id,
        v_student.class_id,
        v_student.section_id,
        p_to_class_id,
        p_to_section_id,
        v_student.academic_session_id,
        p_to_academic_session_id,
        'Promote'
    );

    -- Next roll no in destination class + section + session
    SELECT COALESCE(MAX(roll_no), 0) + 1
    INTO v_next_roll_no
    FROM students
    WHERE school_id = p_school_id
      AND academic_session_id = p_to_academic_session_id
      AND class_id = p_to_class_id
      AND section_id = p_to_section_id
      AND is_deleted = FALSE;

    UPDATE students
    SET class_id = p_to_class_id,
        section_id = p_to_section_id,
        academic_session_id = p_to_academic_session_id,
        roll_no = v_next_roll_no,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_student_id;
END;
$$;

-- List students for a given academic session (READ)
CREATE OR REPLACE FUNCTION fn_students_get_all(
    p_school_id UUID,
    p_academic_session_id UUID,
    p_include_deleted BOOLEAN)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'Id', s.id,
                'AdmissionNo', s.admission_no,
                'FullName', s.full_name,
                'RollNo', s.roll_no,
                'FirstName', s.first_name,
                'MiddleName', s.middle_name,
                'LastName', s.last_name,
                'ClassId', s.class_id,
                'SectionId', s.section_id,
                'AcademicSessionId', s.academic_session_id,
                'DateOfBirth', s.date_of_birth,
                'Email', s.email,
                'ParentMobileNo', s.parent_mobile_no,
                'FatherName', s.father_name,
                'MotherName', s.mother_name,
                'AddressLine', s.address_line,
                'BloodGroup', s.blood_group,
                'AadharNo', s.aadhar_no,
                'UdiseNo', s.udise_no,
                'FatherAadharNo', s.father_aadhar_no,
                'MotherAadharNo', s.mother_aadhar_no,
                'FatherOccupation', s.father_occupation,
                'MotherOccupation', s.mother_occupation,
                'PenNo', s.pen_no,
                'BankName', s.bank_name,
                'BankAccountNo', s.bank_account_no,
                'BankIfsc', s.bank_ifsc,
                'BankBranch', s.bank_branch
            )
            ORDER BY s.full_name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM students s
    WHERE s.school_id = p_school_id
      AND s.academic_session_id = p_academic_session_id
      AND (p_include_deleted OR s.is_deleted = FALSE);

    RETURN v_items;
END;
$$;

-- List students by class and section (READ)
CREATE OR REPLACE FUNCTION fn_students_get_by_class_section(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_include_deleted BOOLEAN DEFAULT FALSE,
    p_academic_session_id UUID DEFAULT NULL)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'Id', s.id,
                'AdmissionNo', s.admission_no,
                'FullName', s.full_name,
                'FirstName', s.first_name,
                'MiddleName', s.middle_name,
                'LastName', s.last_name,
                'ClassId', s.class_id,
                'SectionId', s.section_id,
                'AcademicSessionId', s.academic_session_id,
                'DateOfBirth', s.date_of_birth,
                'Email', s.email,
                'ParentMobileNo', s.parent_mobile_no,
                'FatherName', s.father_name,
                'MotherName', s.mother_name,
                'AddressLine', s.address_line,
                'BloodGroup', s.blood_group,
                'AadharNo', s.aadhar_no,
                'UdiseNo', s.udise_no,
                'FatherAadharNo', s.father_aadhar_no,
                'MotherAadharNo', s.mother_aadhar_no,
                'FatherOccupation', s.father_occupation,
                'MotherOccupation', s.mother_occupation,
                'PenNo', s.pen_no,
                'BankName', s.bank_name,
                'BankAccountNo', s.bank_account_no,
                'BankIfsc', s.bank_ifsc,
                'BankBranch', s.bank_branch
            )
            ORDER BY s.full_name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM students s
    WHERE s.school_id = p_school_id
      AND s.class_id = p_class_id
      AND s.section_id = p_section_id
      AND (p_academic_session_id IS NULL OR s.academic_session_id = p_academic_session_id)
      AND (p_include_deleted OR s.is_deleted = FALSE);

    RETURN v_items;
END;
$$;

-- Update student details
CREATE OR REPLACE PROCEDURE sp_student_update(
    p_school_id UUID,
    p_student_id UUID,
    p_first_name TEXT,
    p_middle_name TEXT,
    p_last_name TEXT,
    p_date_of_birth TIMESTAMPTZ,
    p_email TEXT,
    p_parent_mobile_no TEXT,
    p_father_name TEXT,
    p_mother_name TEXT,
    p_address_line TEXT,
    p_blood_group TEXT,
    p_aadhar_no TEXT,
    p_udise_no TEXT,
    p_father_aadhar_no TEXT,
    p_mother_aadhar_no TEXT,
    p_father_occupation TEXT,
    p_mother_occupation TEXT,
    p_pen_no TEXT,
    p_bank_name TEXT,
    p_bank_account_no TEXT,
    p_bank_ifsc TEXT,
    p_bank_branch TEXT,
    p_class_id UUID,
    p_section_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_student RECORD;
    v_existing INT;
    v_full_name TEXT;
BEGIN
    SELECT *
    INTO v_student
    FROM students
    WHERE id = p_student_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Student not found';
    END IF;

    IF p_aadhar_no IS NOT NULL AND trim(p_aadhar_no) <> '' THEN
        SELECT COUNT(*)
        INTO v_existing
        FROM students
        WHERE school_id = p_school_id
          AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
              = regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')
          AND regexp_replace(regexp_replace(trim(COALESCE(p_aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> ''
          AND id <> p_student_id
          AND is_deleted = FALSE;

        IF v_existing > 0 THEN
            RAISE EXCEPTION 'Duplicate student Aadhaar number';
        END IF;
    END IF;

    v_full_name := trim(both ' ' from coalesce(p_first_name, '') || ' ' || coalesce(p_middle_name, '') || ' ' || coalesce(p_last_name, ''));

    UPDATE students
    SET first_name       = p_first_name,
        middle_name      = p_middle_name,
        last_name        = p_last_name,
        full_name        = v_full_name,
        date_of_birth    = p_date_of_birth::DATE,
        email            = p_email,
        parent_mobile_no = p_parent_mobile_no,
        father_name      = p_father_name,
        mother_name      = p_mother_name,
        address_line     = p_address_line,
        blood_group      = p_blood_group,
        aadhar_no        = p_aadhar_no,
        udise_no         = p_udise_no,
        father_aadhar_no = p_father_aadhar_no,
        mother_aadhar_no = p_mother_aadhar_no,
        father_occupation = p_father_occupation,
        mother_occupation = p_mother_occupation,
        pen_no           = p_pen_no,
        bank_name        = p_bank_name,
        bank_account_no  = p_bank_account_no,
        bank_ifsc        = p_bank_ifsc,
        bank_branch      = p_bank_branch,
        class_id         = p_class_id,
        section_id       = p_section_id,
        updated_at       = NOW(),
        updated_by       = p_user_id
    WHERE id = p_student_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
END;
$$;

-- Soft delete student
CREATE OR REPLACE PROCEDURE sp_student_soft_delete(
    p_school_id UUID,
    p_student_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_student RECORD;
BEGIN
    SELECT *
    INTO v_student
    FROM students
    WHERE id = p_student_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Student not found';
    END IF;

    UPDATE students
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = p_student_id;
END;
$$;


