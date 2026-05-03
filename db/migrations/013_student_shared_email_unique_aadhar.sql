-- Allow the same parent email on multiple students (siblings).
-- Enforce uniqueness per school on normalized student Aadhaar instead.

ALTER TABLE students DROP CONSTRAINT IF EXISTS uq_students_email;

CREATE UNIQUE INDEX IF NOT EXISTS uq_students_school_aadhar_norm ON students (school_id, (regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')))
WHERE is_deleted = FALSE AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> '';

-- After applying, re-run stored_procedures_students.sql (fn_student_admit, sp_student_admit, sp_student_update) if not already updated.
