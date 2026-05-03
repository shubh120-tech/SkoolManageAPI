-- REPORTING FUNCTIONS (READ-ONLY, RETURNING JSONB)

-- Paged list of students for a school with optional search.

CREATE OR REPLACE FUNCTION fn_reporting_students_list(
    p_school_id UUID,
    p_page INT,
    p_page_size INT,
    p_search TEXT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_offset INT;
    v_total INT;
    v_items JSONB;
BEGIN
    IF p_page < 1 THEN
        p_page := 1;
    END IF;
    IF p_page_size < 1 THEN
        p_page_size := 20;
    END IF;

    v_offset := (p_page - 1) * p_page_size;

    SELECT COUNT(*)
    INTO v_total
    FROM students s
    WHERE s.school_id = p_school_id
      AND s.is_deleted = FALSE
      AND (p_search IS NULL
           OR s.full_name ILIKE '%' || p_search || '%'
           OR s.admission_no ILIKE '%' || p_search || '%');

    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', s.id,
            'AdmissionNo', s.admission_no,
            'FullName', s.full_name,
            'ClassId', s.class_id,
            'SectionId', s.section_id,
            'Email', s.email
        )
    ), '[]'::jsonb)
    INTO v_items
    FROM (
        SELECT *
        FROM students
        WHERE school_id = p_school_id
          AND is_deleted = FALSE
          AND (p_search IS NULL
               OR full_name ILIKE '%' || p_search || '%'
               OR admission_no ILIKE '%' || p_search || '%')
        ORDER BY created_at DESC
        OFFSET v_offset
        LIMIT p_page_size
    ) s;

    RETURN jsonb_build_object(
        'TotalCount', v_total,
        'Page', p_page,
        'PageSize', p_page_size,
        'Items', v_items
    );
END;
$$;

-- Monthly attendance summary for a student

CREATE OR REPLACE FUNCTION fn_reporting_student_attendance_monthly(
    p_school_id UUID,
    p_student_id UUID,
    p_year INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Month', m,
            'PresentDays', present_days,
            'TotalDays', total_days,
            'AbsentDays', GREATEST(total_days - present_days, 0),
            'Percentage',
                CASE WHEN total_days > 0
                     THEN ROUND((present_days::NUMERIC * 100) / total_days, 2)
                     ELSE 0
                END
        )
        ORDER BY m
    ), '[]'::jsonb)
    INTO v_items
    FROM (
        SELECT
            EXTRACT(MONTH FROM ad.attendance_date)::INT AS m,
            COUNT(*) AS total_days,
            SUM(CASE WHEN ar.is_present THEN 1 ELSE 0 END) AS present_days
        FROM attendance_days ad
        JOIN attendance_records ar
          ON ar.attendance_day_id = ad.id
         AND ar.school_id = ad.school_id
         AND ar.is_deleted = FALSE
        WHERE ad.school_id = p_school_id
          AND ad.is_deleted = FALSE
          AND ad.attendance_date >= make_date(p_year, 1, 1)
          AND ad.attendance_date < make_date(p_year + 1, 1, 1)
          AND ar.student_id = p_student_id
        GROUP BY EXTRACT(MONTH FROM ad.attendance_date)
    ) t;

    RETURN jsonb_build_object(
        'StudentId', p_student_id,
        'Year', p_year,
        'Items', COALESCE(v_items, '[]'::jsonb)
    );
END;
$$;

-- Student attendance detail for a given month (per day).

CREATE OR REPLACE FUNCTION fn_reporting_student_attendance_month_detail(
    p_school_id UUID,
    p_student_id UUID,
    p_year INT,
    p_month INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Date', ad.attendance_date,
            'IsPresent', ar.is_present,
            'ClassId', ad.class_id,
            'SectionId', ad.section_id
        )
        ORDER BY ad.attendance_date
    ), '[]'::jsonb)
    INTO v_items
    FROM attendance_days ad
    JOIN attendance_records ar
      ON ar.attendance_day_id = ad.id
     AND ar.school_id = ad.school_id
     AND ar.is_deleted = FALSE
    WHERE ad.school_id = p_school_id
      AND ad.is_deleted = FALSE
      AND EXTRACT(YEAR FROM ad.attendance_date)::INT = p_year
      AND EXTRACT(MONTH FROM ad.attendance_date)::INT = p_month
      AND ar.student_id = p_student_id;

    RETURN jsonb_build_object(
        'StudentId', p_student_id,
        'Year', p_year,
        'Month', p_month,
        'Items', COALESCE(v_items, '[]'::jsonb)
    );
END;
$$;

-- Student overview: basic info + attendance % + fee summary.

CREATE OR REPLACE FUNCTION fn_reporting_student_overview(
    p_school_id UUID,
    p_student_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_student RECORD;
    v_class_name TEXT;
    v_section_name TEXT;
    v_total_days INT;
    v_present_days INT;
    v_attendance_pct NUMERIC(5,2);
    v_total_due NUMERIC(12,2);
    v_total_paid NUMERIC(12,2);
    v_outstanding NUMERIC(12,2);
BEGIN
    SELECT *
    INTO v_student
    FROM students
    WHERE id = p_student_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    SELECT name INTO v_class_name
    FROM classes
    WHERE id = v_student.class_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    SELECT name INTO v_section_name
    FROM sections
    WHERE id = v_student.section_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    SELECT COUNT(DISTINCT ad.id)
    INTO v_total_days
    FROM attendance_days ad
    JOIN attendance_records ar ON ar.attendance_day_id = ad.id AND ar.is_deleted = FALSE
    WHERE ad.school_id = p_school_id
      AND ad.is_deleted = FALSE
      AND ar.student_id = p_student_id;

    SELECT COUNT(*)
    INTO v_present_days
    FROM attendance_days ad
    JOIN attendance_records ar ON ar.attendance_day_id = ad.id AND ar.is_deleted = FALSE
    WHERE ad.school_id = p_school_id
      AND ad.is_deleted = FALSE
      AND ar.student_id = p_student_id
      AND ar.is_present = TRUE;

    IF v_total_days > 0 THEN
        v_attendance_pct := ROUND((v_present_days::NUMERIC / v_total_days::NUMERIC) * 100.0, 2);
    ELSE
        v_attendance_pct := 0;
    END IF;

    SELECT COALESCE(SUM(amount - COALESCE(discount_amount,0) + COALESCE(late_fine_amount,0)), 0)
    INTO v_total_due
    FROM student_fee_assignments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND is_deleted = FALSE;

    SELECT COALESCE(SUM(amount_paid), 0)
    INTO v_total_paid
    FROM fee_payments
    WHERE school_id = p_school_id
      AND student_id = p_student_id
      AND is_deleted = FALSE;

    v_outstanding := v_total_due - v_total_paid;

    RETURN jsonb_build_object(
        'Student', jsonb_build_object(
            'Id', v_student.id,
            'AdmissionNo', v_student.admission_no,
            'FullName', v_student.full_name,
            'Email', v_student.email,
            'ClassId', v_student.class_id,
            'SectionId', v_student.section_id,
            'ClassName', v_class_name,
            'SectionName', v_section_name,
            'ParentMobileNo', v_student.parent_mobile_no
        ),
        'AttendancePercentage', v_attendance_pct,
        'TotalFeeDue', v_total_due,
        'TotalFeePaid', v_total_paid,
        'Outstanding', v_outstanding
    );
END;
$$;

-- Staff payroll summary for a given year.

CREATE OR REPLACE FUNCTION fn_reporting_staff_payroll_summary(
    p_school_id UUID,
    p_staff_id UUID,
    p_year INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_staff RECORD;
    v_items JSONB;
BEGIN
    SELECT *
    INTO v_staff
    FROM staff
    WHERE id = p_staff_id
      AND school_id = p_school_id
      AND is_deleted = FALSE;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Month', pr.month,
            'Year', pr.year,
            'GrossAmount', pr.gross_amount,
            'NetAmount', pr.net_amount,
            'GeneratedOn', pr.generated_on,
            'PreviousPending', pr.previous_pending,
            'TotalDue', pr.total_due,
            'TotalPaid', pr.total_paid,
            'PendingAmount', pr.pending_amount
        )
        ORDER BY pr.year, pr.month
    ), '[]'::jsonb)
    INTO v_items
    FROM payroll_records pr
    WHERE pr.school_id = p_school_id
      AND pr.staff_id = p_staff_id
      AND pr.year = p_year
      AND pr.is_deleted = FALSE;

    RETURN jsonb_build_object(
        'Staff', jsonb_build_object(
            'Id', v_staff.id,
            'StaffCode', v_staff.staff_code,
            'FullName', v_staff.full_name,
            'Email', v_staff.email
        ),
        'Year', p_year,
        'Payroll', v_items
    );
END;
$$;

-- Timetable view for a class/section.

CREATE OR REPLACE FUNCTION fn_reporting_timetable_for_class(
    p_school_id UUID,
    p_class_id UUID,
    p_section_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', t.id,
            'ClassId', t.class_id,
            'SectionId', t.section_id,
            'SubjectId', t.subject_id,
            'TeacherId', t.teacher_id,
            'TeacherName', st.full_name,
            'DayOfWeek', t.day_of_week,
            'StartTime', t.start_time,
            'EndTime', t.end_time
        )
        ORDER BY t.day_of_week, t.start_time
    ), '[]'::jsonb)
    INTO v_items
    FROM timetables t
    LEFT JOIN staff st
      ON st.id = t.teacher_id
     AND st.school_id = p_school_id
     AND st.is_deleted = FALSE
    WHERE t.school_id = p_school_id
      AND t.class_id = p_class_id
      AND t.section_id = p_section_id
      AND t.is_deleted = FALSE;

    RETURN jsonb_build_object(
        'ClassId', p_class_id,
        'SectionId', p_section_id,
        'Entries', v_items
    );
END;
$$;

-- Dashboard summary: total students, staff, collection, pending.

CREATE OR REPLACE FUNCTION fn_reporting_dashboard_summary(
    p_school_id UUID)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_total_students INT;
    v_total_staff INT;
    v_total_collection NUMERIC(12,2);
    v_monthly_collection NUMERIC(12,2);
    v_total_due NUMERIC(12,2);
    v_total_paid NUMERIC(12,2);
    v_total_pending NUMERIC(12,2);
    v_month_start DATE;
    v_next_month DATE;
    v_today DATE;
    v_today_marked INT;
    v_today_present INT;
    v_today_pct NUMERIC(5,2);
BEGIN
    SELECT COUNT(*) INTO v_total_students
    FROM students
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    SELECT COUNT(*) INTO v_total_staff
    FROM staff
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    -- Lifetime collection
    SELECT COALESCE(SUM(amount_paid), 0) INTO v_total_collection
    FROM fee_payments
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    -- Monthly collection (current month)
    v_month_start := date_trunc('month', NOW())::date;
    v_next_month := (v_month_start + INTERVAL '1 month')::date;
    SELECT COALESCE(SUM(amount_paid), 0) INTO v_monthly_collection
    FROM fee_payments
    WHERE school_id = p_school_id
      AND is_deleted = FALSE
      AND payment_date >= v_month_start
      AND payment_date < v_next_month;

    -- Due vs paid
    SELECT COALESCE(SUM(amount - COALESCE(discount_amount,0) + COALESCE(late_fine_amount,0)), 0)
    INTO v_total_due
    FROM student_fee_assignments
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    SELECT COALESCE(SUM(amount_paid), 0)
    INTO v_total_paid
    FROM fee_payments
    WHERE school_id = p_school_id
      AND is_deleted = FALSE;

    v_total_pending := v_total_due - v_total_paid;

    -- Today's attendance (across all classes)
    v_today := NOW()::date;
    SELECT COUNT(*) INTO v_today_marked
    FROM attendance_days ad
    JOIN attendance_records ar ON ar.attendance_day_id = ad.id AND ar.is_deleted = FALSE
    WHERE ad.school_id = p_school_id
      AND ad.is_deleted = FALSE
      AND ad.attendance_date = v_today;

    SELECT COUNT(*) INTO v_today_present
    FROM attendance_days ad
    JOIN attendance_records ar ON ar.attendance_day_id = ad.id AND ar.is_deleted = FALSE
    WHERE ad.school_id = p_school_id
      AND ad.is_deleted = FALSE
      AND ad.attendance_date = v_today
      AND ar.is_present = TRUE;

    IF v_today_marked > 0 THEN
        v_today_pct := ROUND((v_today_present::NUMERIC / v_today_marked::NUMERIC) * 100.0, 2);
    ELSE
        v_today_pct := 0;
    END IF;

    RETURN jsonb_build_object(
        'TotalStudents', v_total_students,
        'TotalStaff', v_total_staff,
        'TotalCollection', v_total_collection,
        'MonthlyCollection', v_monthly_collection,
        'TotalPending', v_total_pending,
        'TodayAttendancePercentage', v_today_pct,
        'TodayPresentCount', v_today_present,
        'TodayMarkedCount', v_today_marked
    );
END;
$$;

-- Staff attendance: monthly summary per staff (per-day attendance aggregated by month).

CREATE OR REPLACE FUNCTION fn_reporting_staff_attendance_monthly(
    p_school_id UUID,
    p_staff_id UUID,
    p_year INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Month', m,
            'PresentDays', present_days,
            'TotalDays', total_days,
            'AbsentDays', GREATEST(total_days - present_days, 0),
            'Percentage',
                CASE WHEN total_days > 0
                     THEN ROUND((present_days::NUMERIC * 100) / total_days, 2)
                     ELSE 0
                END
        )
        ORDER BY m
    ), '[]'::jsonb)
    INTO v_items
    FROM (
        SELECT
            EXTRACT(MONTH FROM sad.attendance_date)::INT AS m,
            COUNT(*) AS total_days,
            SUM(CASE WHEN sar.is_present THEN 1 ELSE 0 END) AS present_days
        FROM staff_attendance_days sad
        JOIN staff_attendance_records sar
          ON sar.attendance_day_id = sad.id
         AND sar.school_id = sad.school_id
         AND sar.is_deleted = FALSE
        WHERE sad.school_id = p_school_id
          AND sad.is_deleted = FALSE
          AND sad.attendance_date >= make_date(p_year, 1, 1)
          AND sad.attendance_date < make_date(p_year + 1, 1, 1)
          AND sar.staff_id = p_staff_id
        GROUP BY EXTRACT(MONTH FROM sad.attendance_date)
    ) t;

    RETURN jsonb_build_object(
        'StaffId', p_staff_id,
        'Year', p_year,
        'Items', COALESCE(v_items, '[]'::jsonb)
    );
END;
$$;

-- Staff attendance: per-day detail for a given staff and month.

CREATE OR REPLACE FUNCTION fn_reporting_staff_attendance_month_detail(
    p_school_id UUID,
    p_staff_id UUID,
    p_year INT,
    p_month INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Date', sad.attendance_date,
            'IsPresent', sar.is_present
        )
        ORDER BY sad.attendance_date
    ), '[]'::jsonb)
    INTO v_items
    FROM staff_attendance_days sad
    JOIN staff_attendance_records sar
      ON sar.attendance_day_id = sad.id
     AND sar.school_id = sad.school_id
     AND sar.is_deleted = FALSE
    WHERE sad.school_id = p_school_id
      AND sad.is_deleted = FALSE
      AND EXTRACT(YEAR FROM sad.attendance_date)::INT = p_year
      AND EXTRACT(MONTH FROM sad.attendance_date)::INT = p_month
      AND sar.staff_id = p_staff_id;

    RETURN jsonb_build_object(
        'StaffId', p_staff_id,
        'Year', p_year,
        'Month', p_month,
        'Items', COALESCE(v_items, '[]'::jsonb)
    );
END;
$$;

-- Global search across students and staff (for dashboard).

CREATE OR REPLACE FUNCTION fn_reporting_global_search(
    p_school_id UUID,
    p_query TEXT,
    p_limit INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_limit INT := COALESCE(NULLIF(p_limit, 0), 10);
    v_students JSONB;
    v_staff JSONB;
BEGIN
    IF p_query IS NULL OR btrim(p_query) = '' THEN
        p_query := '';
    END IF;

    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', s.id,
            'AdmissionNo', s.admission_no,
            'FullName', s.full_name,
            'Email', s.email,
            'MobileNo', s.parent_mobile_no,
            'ClassId', s.class_id,
            'ClassName', c.name,
            'SectionId', s.section_id,
            'SectionName', sec.name,
            'PendingAmount',
                COALESCE(
                    (fn_student_fee_summary_get(p_school_id, s.id)->>'Pending')::NUMERIC,
                    0
                )
        )
    ), '[]'::jsonb)
    INTO v_students
    FROM (
        SELECT *
        FROM students
        WHERE school_id = p_school_id
          AND is_deleted = FALSE
          AND (
            p_query = '' OR
            full_name ILIKE '%' || p_query || '%' OR
            admission_no ILIKE '%' || p_query || '%' OR
            email ILIKE '%' || p_query || '%' OR
            parent_mobile_no ILIKE '%' || p_query || '%'
          )
        ORDER BY created_at DESC
        LIMIT v_limit
    ) s
    LEFT JOIN classes c
      ON c.id = s.class_id
     AND c.school_id = s.school_id
     AND c.is_deleted = FALSE
    LEFT JOIN sections sec
      ON sec.id = s.section_id
     AND sec.school_id = s.school_id
     AND sec.is_deleted = FALSE;

    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Id', st.id,
            'StaffCode', st.staff_code,
            'FullName', st.full_name,
            'Email', st.email,
            'MobileNo', st.mobile_no,
            'IsTeaching', st.is_teaching
        )
    ), '[]'::jsonb)
    INTO v_staff
    FROM (
        SELECT *
        FROM staff
        WHERE school_id = p_school_id
          AND is_deleted = FALSE
          AND (
            p_query = '' OR
            full_name ILIKE '%' || p_query || '%' OR
            staff_code ILIKE '%' || p_query || '%' OR
            email ILIKE '%' || p_query || '%' OR
            mobile_no ILIKE '%' || p_query || '%'
          )
        ORDER BY created_at DESC
        LIMIT v_limit
    ) st;

    RETURN jsonb_build_object(
        'Students', v_students,
        'Staff', v_staff
    );
END;
$$;

-- Paged list: when p_month is set, shows ALL staff with optional payroll for that month/year.
-- When p_month is null, shows only staff that have at least one payroll record for the year (legacy behavior).

CREATE OR REPLACE FUNCTION fn_reporting_payroll_list(
    p_school_id UUID,
    p_year INT,
    p_month INT,
    p_page INT,
    p_page_size INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_page INT := COALESCE(NULLIF(p_page, 0), 1);
    v_page_size INT := COALESCE(NULLIF(p_page_size, 0), 10);
    v_offset INT := (v_page - 1) * v_page_size;
    v_total INT;
    v_items JSONB;
BEGIN
    IF p_month IS NOT NULL AND p_year IS NOT NULL THEN
        -- All staff for school, left join payroll for selected month/year; one row per staff.
        SELECT COUNT(*) INTO v_total
        FROM staff st
        WHERE st.school_id = p_school_id
          AND st.is_deleted = FALSE;

        SELECT jsonb_agg(sub.rec ORDER BY sub.rec->>'FullName')
        INTO v_items
        FROM (
            SELECT jsonb_build_object(
                'PayrollRecordId', pr.id,
                'StaffId', st.id,
                'StaffCode', st.staff_code,
                'FullName', st.full_name,
                'Month', COALESCE(pr.month, p_month),
                'Year', COALESCE(pr.year, p_year),
                'GrossAmount', COALESCE(pr.gross_amount, 0),
                'Deduction', (COALESCE(pr.gross_amount, 0) - COALESCE(pr.net_amount, 0)),
                'NetAmount', COALESCE(pr.net_amount, 0),
                'GeneratedOn', pr.generated_on,
                'PreviousPending', COALESCE(pr.previous_pending, 0),
                'TotalDue', COALESCE(pr.total_due, 0),
                'TotalPaid', COALESCE(pr.total_paid, 0),
                'PendingAmount', COALESCE(pr.pending_amount, 0)
            ) AS rec
            FROM staff st
            LEFT JOIN payroll_records pr
              ON pr.staff_id = st.id
             AND pr.school_id = st.school_id
             AND pr.is_deleted = FALSE
             AND pr.year = p_year
             AND pr.month = p_month
            WHERE st.school_id = p_school_id
              AND st.is_deleted = FALSE
            ORDER BY st.full_name
            OFFSET v_offset
            LIMIT v_page_size
        ) sub;
        v_items := COALESCE(v_items, '[]'::jsonb);
    ELSE
        -- Legacy: only staff that have payroll records for the year.
        SELECT COUNT(*) INTO v_total
        FROM payroll_records pr
        JOIN staff st ON st.id = pr.staff_id
        WHERE pr.school_id = p_school_id
          AND pr.is_deleted = FALSE
          AND st.is_deleted = FALSE
          AND (p_year IS NULL OR pr.year = p_year)
          AND (p_month IS NULL OR pr.month = p_month);

        SELECT COALESCE(jsonb_agg(
            jsonb_build_object(
                'PayrollRecordId', pr.id,
                'StaffId', pr.staff_id,
                'StaffCode', st.staff_code,
                'FullName', st.full_name,
                'Month', pr.month,
                'Year', pr.year,
                'GrossAmount', pr.gross_amount,
                'Deduction', (pr.gross_amount - pr.net_amount),
                'NetAmount', pr.net_amount,
                'GeneratedOn', pr.generated_on,
                'PreviousPending', pr.previous_pending,
                'TotalDue', pr.total_due,
                'TotalPaid', pr.total_paid,
                'PendingAmount', pr.pending_amount
            )
            ORDER BY pr.year DESC, pr.month DESC, st.full_name
        ), '[]'::jsonb)
        INTO v_items
        FROM payroll_records pr
        JOIN staff st ON st.id = pr.staff_id
        WHERE pr.school_id = p_school_id
          AND pr.is_deleted = FALSE
          AND st.is_deleted = FALSE
          AND (p_year IS NULL OR pr.year = p_year)
          AND (p_month IS NULL OR pr.month = p_month)
        OFFSET v_offset
        LIMIT v_page_size;
    END IF;

    RETURN jsonb_build_object(
        'TotalCount', COALESCE(v_total, 0),
        'Page', v_page,
        'PageSize', v_page_size,
        'Items', COALESCE(v_items, '[]'::jsonb)
    );
END;
$$;

-- Recent activity: last N events across students and fee payments.

CREATE OR REPLACE FUNCTION fn_reporting_recent_activity(
    p_school_id UUID,
    p_limit INT)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_limit INT := COALESCE(NULLIF(p_limit, 0), 10);
    v_items JSONB;
BEGIN
    SELECT COALESCE(jsonb_agg(
        jsonb_build_object(
            'Type', Type,
            'Title', Title,
            'Description', Description,
            'OccurredAt', OccurredAt
        )
        ORDER BY OccurredAt DESC
    ), '[]'::jsonb)
    INTO v_items
    FROM (
        -- Recent fee payments
        SELECT
            'FeePayment' AS Type,
            'Fee payment received' AS Title,
            format('₹%s from %s (%s)', fp.amount_paid, COALESCE(s.full_name, ''), COALESCE(s.admission_no, '')) AS Description,
            fp.payment_date AS OccurredAt
        FROM fee_payments fp
        LEFT JOIN students s
          ON s.id = fp.student_id
         AND s.school_id = fp.school_id
        WHERE fp.school_id = p_school_id
          AND fp.is_deleted = FALSE

        UNION ALL

        -- Recent student admissions
        SELECT
            'StudentAdmitted' AS Type,
            'New student admitted' AS Title,
            format('%s (%s)', COALESCE(st.full_name, ''), COALESCE(st.admission_no, '')) AS Description,
            st.created_at AS OccurredAt
        FROM students st
        WHERE st.school_id = p_school_id
          AND st.is_deleted = FALSE
    ) AS events
    ORDER BY OccurredAt DESC
    LIMIT v_limit;

    RETURN v_items;
END;
$$;

