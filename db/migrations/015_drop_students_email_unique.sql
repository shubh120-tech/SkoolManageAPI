-- Allow multiple students (siblings) to share the same parent/guardian email.
-- Drops the old unique constraint whether it was named explicitly or by PostgreSQL defaults.

ALTER TABLE students DROP CONSTRAINT IF EXISTS uq_students_email;
ALTER TABLE students DROP CONSTRAINT IF EXISTS students_school_id_email_key;

DO $$
DECLARE
  r RECORD;
BEGIN
  FOR r IN
    SELECT c.conname
    FROM pg_constraint c
    JOIN pg_class t ON c.conrelid = t.oid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    WHERE n.nspname = 'public'
      AND t.relname = 'students'
      AND c.contype = 'u'
      AND pg_get_constraintdef(c.oid) LIKE '%UNIQUE (school_id, email)%'
  LOOP
    EXECUTE format('ALTER TABLE students DROP CONSTRAINT IF EXISTS %I', r.conname);
  END LOOP;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_students_school_aadhar_norm ON students (school_id, (regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g')))
WHERE is_deleted = FALSE AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') <> '';
