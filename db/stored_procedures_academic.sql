-- ACADEMIC PROCEDURES AND FUNCTIONS (CLASSES & SECTIONS)

-- Create class (scoped by academic session)
CREATE OR REPLACE PROCEDURE sp_class_create(
    p_school_id UUID,
    p_academic_session_id UUID,
    p_name TEXT,
    p_capacity INT,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_exists INT;
    v_session RECORD;
BEGIN
    IF p_capacity <= 0 THEN
        RAISE EXCEPTION 'Capacity must be positive';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions
    WHERE id = p_academic_session_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Academic session not found for this school';
    END IF;
    -- Classes may be created for inactive sessions (e.g. new session setup before activation).

    SELECT COUNT(*) INTO v_exists
    FROM classes
    WHERE school_id = p_school_id
      AND academic_session_id = p_academic_session_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Class name already exists for this session';
    END IF;

    INSERT INTO classes(
        id, school_id, is_deleted, created_at, created_by,
        name, capacity, academic_session_id)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_name,
        p_capacity,
        p_academic_session_id
    );
END;
$$;

-- Update class
CREATE OR REPLACE PROCEDURE sp_class_update(
    p_school_id UUID,
    p_class_id UUID,
    p_academic_session_id UUID,
    p_name TEXT,
    p_capacity INT,
    p_updated_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_class RECORD;
    v_exists INT;
    v_session RECORD;
BEGIN
    IF p_capacity <= 0 THEN
        RAISE EXCEPTION 'Capacity must be positive';
    END IF;

    -- Always use the active academic session for this school, ignore passed id
    SELECT *
    INTO v_session
    FROM academic_sessions
    WHERE school_id = p_school_id
      AND is_deleted = FALSE
      AND is_active = TRUE
    LIMIT 1;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Academic session not active for this school';
    END IF;

    SELECT * INTO v_class
    FROM classes
    WHERE id = p_class_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM classes
    WHERE school_id = p_school_id
      AND academic_session_id = v_session.id
      AND LOWER(name) = LOWER(p_name)
      AND id <> p_class_id
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Class name already exists for this session';
    END IF;

    UPDATE classes
    SET name = p_name,
        capacity = p_capacity,
        academic_session_id = v_session.id,
        updated_at = NOW(),
        updated_by = p_updated_by
    WHERE id = p_class_id;
END;
$$;

-- Create section (inherits academic session from class)
CREATE OR REPLACE PROCEDURE sp_section_create(
    p_school_id UUID,
    p_class_id UUID,
    p_name TEXT,
    p_capacity INT,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_class RECORD;
    v_exists INT;
BEGIN
    IF p_capacity <= 0 THEN
        RAISE EXCEPTION 'Capacity must be positive';
    END IF;

    SELECT * INTO v_class
    FROM classes
    WHERE id = p_class_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found for this school';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM sections
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND academic_session_id = v_class.academic_session_id
      AND LOWER(name) = LOWER(p_name)
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Section name already exists for this class and session';
    END IF;

    INSERT INTO sections(
        id, school_id, is_deleted, created_at, created_by,
        class_id, name, capacity, academic_session_id)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_created_by,
        p_class_id,
        p_name,
        p_capacity,
        v_class.academic_session_id
    );
END;
$$;

-- Update section
CREATE OR REPLACE PROCEDURE sp_section_update(
    p_school_id UUID,
    p_section_id UUID,
    p_name TEXT,
    p_capacity INT,
    p_updated_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_section RECORD;
    v_exists INT;
BEGIN
    IF p_capacity <= 0 THEN
        RAISE EXCEPTION 'Capacity must be positive';
    END IF;

    SELECT * INTO v_section
    FROM sections
    WHERE id = p_section_id
      AND school_id = p_school_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;

    SELECT COUNT(*) INTO v_exists
    FROM sections
    WHERE school_id = p_school_id
      AND class_id = v_section.class_id
      AND academic_session_id = v_section.academic_session_id
      AND LOWER(name) = LOWER(p_name)
      AND id <> p_section_id
      AND is_deleted = FALSE;

    IF v_exists > 0 THEN
        RAISE EXCEPTION 'Section name already exists for this class and session';
    END IF;

    UPDATE sections
    SET name = p_name,
        capacity = p_capacity,
        updated_at = NOW(),
        updated_by = p_updated_by
    WHERE id = p_section_id;
END;
$$;

-- READ FUNCTIONS (JSONB)

CREATE OR REPLACE FUNCTION fn_classes_get_all(
    p_school_id UUID,
    p_academic_session_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', c.id,
            'Name', c.name,
            'Capacity', c.capacity
        )
        ORDER BY c.name
    ), '[]'::jsonb)
    INTO v_items
    FROM classes c
    WHERE c.school_id = p_school_id
      AND c.academic_session_id = p_academic_session_id
      AND c.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

CREATE OR REPLACE FUNCTION fn_sections_get_by_class(
    p_school_id UUID,
    p_class_id UUID,
    p_academic_session_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', s.id,
            'ClassId', s.class_id,
            'Name', s.name,
            'Capacity', s.capacity
        )
        ORDER BY s.name
    ), '[]'::jsonb)
    INTO v_items
    FROM sections s
    WHERE s.school_id = p_school_id
      AND s.class_id = p_class_id
      AND s.academic_session_id = p_academic_session_id
      AND s.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

-- CLASS TEACHER ASSIGNMENT

-- Rule: one teacher can be class teacher for one or more classes (one-to-many);
--       each (class, section, session) has only one class teacher (uq_class_teachers).
CREATE OR REPLACE PROCEDURE sp_class_teacher_assign(
    p_school_id UUID,
    p_teacher_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_academic_session_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_school RECORD;
    v_teacher RECORD;
    v_class RECORD;
    v_section RECORD;
    v_session RECORD;
    v_slot RECORD;
    v_constraint_name TEXT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_class_teacher_assign_' || p_school_id::text));

    SELECT * INTO v_school FROM schools WHERE id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_school.is_active OR v_school.is_soft_deleted THEN
        RAISE EXCEPTION 'School invalid or inactive';
    END IF;

    IF v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW() THEN
        RAISE EXCEPTION 'School license expired';
    END IF;

    SELECT * INTO v_teacher
    FROM staff
    WHERE id = p_teacher_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Teacher not found';
    END IF;

    IF NOT v_teacher.is_teaching THEN
        RAISE EXCEPTION 'Staff is not marked as teaching';
    END IF;

    SELECT * INTO v_class
    FROM classes WHERE id = p_class_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class not found';
    END IF;
    IF v_class.academic_session_id IS DISTINCT FROM p_academic_session_id THEN
        RAISE EXCEPTION 'Class does not belong to the selected academic session';
    END IF;

    SELECT * INTO v_section
    FROM sections WHERE id = p_section_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Section not found';
    END IF;
    IF v_section.class_id IS DISTINCT FROM p_class_id OR v_section.academic_session_id IS DISTINCT FROM p_academic_session_id THEN
        RAISE EXCEPTION 'Section does not belong to the selected class/session';
    END IF;

    SELECT * INTO v_session
    FROM academic_sessions WHERE id = p_academic_session_id AND school_id = p_school_id AND is_deleted = FALSE;
    IF NOT FOUND OR NOT v_session.is_active THEN
        RAISE EXCEPTION 'Academic session not active';
    END IF;

    -- This (class, section, session) may have at most one teacher (non-deleted)
    SELECT id, teacher_id INTO v_slot
    FROM class_teachers
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = FALSE
    LIMIT 1;

    IF FOUND THEN
        IF v_slot.teacher_id = p_teacher_id THEN
            RETURN;
        END IF;
        RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
    END IF;

    -- Reactivate a soft-deleted row for this slot if it exists (unique constraint includes deleted rows)
    UPDATE class_teachers
    SET is_deleted = FALSE,
        teacher_id = p_teacher_id,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = TRUE;

    IF FOUND THEN
        RETURN;
    END IF;

    INSERT INTO class_teachers(
        id, school_id, is_deleted, created_at, created_by,
        class_id, section_id, teacher_id, academic_session_id)
    VALUES (
        uuid_generate_v4(),
        p_school_id,
        FALSE,
        NOW(),
        p_user_id,
        p_class_id,
        p_section_id,
        p_teacher_id,
        p_academic_session_id
    );
EXCEPTION
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_constraint_name = CONSTRAINT_NAME;
        RAISE EXCEPTION 'This class/section is already assigned to another teacher.';
END;
$$;

CREATE OR REPLACE PROCEDURE sp_class_teacher_unassign(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_academic_session_id UUID,
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_ct RECORD;
BEGIN
    SELECT *
    INTO v_ct
    FROM class_teachers
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND section_id = p_section_id
      AND academic_session_id = p_academic_session_id
      AND is_deleted = FALSE
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class teacher assignment not found';
    END IF;

    UPDATE class_teachers
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE id = v_ct.id;
END;
$$;

CREATE OR REPLACE FUNCTION fn_class_teacher_get(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID,
    p_academic_session_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_result JSONB;
BEGIN
    SELECT to_jsonb(row) INTO v_result
    FROM (
        SELECT
            ct.id AS ClassTeacherId,
            ct.teacher_id AS TeacherId,
            s.full_name AS TeacherName,
            s.email,
            s.mobile_no
        FROM class_teachers ct
        JOIN staff s
          ON s.id = ct.teacher_id
         AND s.school_id = ct.school_id
        WHERE ct.school_id = p_school_id
          AND ct.class_id = p_class_id
          AND ct.section_id = p_section_id
          AND ct.academic_session_id = p_academic_session_id
          AND ct.is_deleted = FALSE
    ) AS row;

    RETURN COALESCE(v_result, '{}'::jsonb);
END;
$$;

-- Get class teacher assignment for a staff (teacher). Returns first assignment found.
CREATE OR REPLACE FUNCTION fn_class_teacher_get_by_teacher(
    p_school_id UUID,
    p_teacher_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_result JSONB;
BEGIN
    SELECT to_jsonb(row) INTO v_result
    FROM (
        SELECT
            ct.class_id AS ClassId,
            ct.section_id AS SectionId,
            ct.academic_session_id AS AcademicSessionId
        FROM class_teachers ct
        WHERE ct.school_id = p_school_id
          AND ct.teacher_id = p_teacher_id
          AND ct.is_deleted = FALSE
        LIMIT 1
    ) AS row;

    RETURN COALESCE(v_result, 'null'::jsonb);
END;
$$;

-- CLASS SUBJECTS (subjects offered for a specific class)

CREATE OR REPLACE PROCEDURE sp_class_subjects_set(
    p_school_id UUID,
    p_class_id UUID,
    p_subject_ids UUID[],
    p_user_id UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    v_subject_id UUID;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('sp_class_subjects_set_' || p_school_id::text || '_' || p_class_id::text));

    -- Soft delete existing mappings for this class
    UPDATE class_subjects
    SET is_deleted = TRUE,
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE school_id = p_school_id
      AND class_id = p_class_id
      AND is_deleted = FALSE;

    IF p_subject_ids IS NULL THEN
        RETURN;
    END IF;

    FOREACH v_subject_id IN ARRAY p_subject_ids LOOP
        -- Validate subject belongs to this school
        PERFORM 1
        FROM subjects
        WHERE id = v_subject_id
          AND school_id = p_school_id
          AND is_deleted = FALSE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Subject % not found for this school', v_subject_id;
        END IF;

        INSERT INTO class_subjects(
            id, school_id, is_deleted, created_at, created_by,
            class_id, subject_id)
        VALUES (
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_user_id,
            p_class_id,
            v_subject_id
        )
        ON CONFLICT (school_id, class_id, subject_id) DO UPDATE
        SET is_deleted = FALSE,
            updated_at = NOW(),
            updated_by = EXCLUDED.created_by;
    END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION fn_class_subjects_get(
    p_school_id UUID,
    p_class_id UUID)
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
                'Name', s.name,
                'Code', s.code
            )
            ORDER BY s.name
        ),
        '[]'::jsonb
    )
    INTO v_items
    FROM class_subjects cs
    JOIN subjects s
      ON s.id = cs.subject_id
     AND s.school_id = cs.school_id
    WHERE cs.school_id = p_school_id
      AND cs.class_id = p_class_id
      AND cs.is_deleted = FALSE
      AND s.is_deleted = FALSE;

    RETURN v_items;
END;
$$;

-- Copy all classes and their sections from one academic session to another (same school).
CREATE OR REPLACE PROCEDURE sp_copy_classes_sections_from_session(
    p_school_id UUID,
    p_from_session_id UUID,
    p_to_session_id UUID,
    p_created_by UUID)
LANGUAGE plpgsql
AS $$
DECLARE
    r_class RECORD;
    r_section RECORD;
    v_new_class_id UUID;
    v_from_exists BOOLEAN;
    v_to_exists BOOLEAN;
BEGIN
    IF p_from_session_id = p_to_session_id THEN
        RAISE EXCEPTION 'Source and target academic session must be different';
    END IF;

    SELECT EXISTS(
        SELECT 1 FROM academic_sessions
        WHERE id = p_from_session_id AND school_id = p_school_id AND is_deleted = FALSE
    ) INTO v_from_exists;
    IF NOT v_from_exists THEN
        RAISE EXCEPTION 'Source academic session not found';
    END IF;

    SELECT EXISTS(
        SELECT 1 FROM academic_sessions
        WHERE id = p_to_session_id AND school_id = p_school_id AND is_deleted = FALSE
    ) INTO v_to_exists;
    IF NOT v_to_exists THEN
        RAISE EXCEPTION 'Target academic session not found';
    END IF;

    FOR r_class IN
        SELECT id, name, capacity
        FROM classes
        WHERE school_id = p_school_id
          AND academic_session_id = p_from_session_id
          AND is_deleted = FALSE
        ORDER BY name
    LOOP
        v_new_class_id := uuid_generate_v4();
        INSERT INTO classes(
            id, school_id, is_deleted, created_at, created_by,
            name, capacity, academic_session_id)
        VALUES (
            v_new_class_id,
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            r_class.name,
            r_class.capacity,
            p_to_session_id
        );

        -- Copy class fee structure rows to the newly created class in target session.
        INSERT INTO class_fee_structures(
            id, school_id, is_deleted, created_at, created_by,
            class_id, fee_head_id, amount, is_optional)
        SELECT
            uuid_generate_v4(),
            p_school_id,
            FALSE,
            NOW(),
            p_created_by,
            v_new_class_id,
            cfs.fee_head_id,
            cfs.amount,
            COALESCE(cfs.is_optional, FALSE)
        FROM class_fee_structures cfs
        WHERE cfs.school_id = p_school_id
          AND cfs.class_id = r_class.id
          AND cfs.is_deleted = FALSE;

        FOR r_section IN
            SELECT name, capacity
            FROM sections
            WHERE school_id = p_school_id
              AND class_id = r_class.id
              AND is_deleted = FALSE
            ORDER BY name
        LOOP
            INSERT INTO sections(
                id, school_id, is_deleted, created_at, created_by,
                class_id, name, capacity, academic_session_id)
            VALUES (
                uuid_generate_v4(),
                p_school_id,
                FALSE,
                NOW(),
                p_created_by,
                v_new_class_id,
                r_section.name,
                r_section.capacity,
                p_to_session_id
            );
        END LOOP;
    END LOOP;
END;
$$;

