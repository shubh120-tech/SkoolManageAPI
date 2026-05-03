-- sp_student_admit / fn_student_admit: extended columns (udise, parent aadhar, occupations, PEN, bank) + roll_no in fn.
-- PostgreSQL creates a new overload if you only CREATE OR REPLACE with a different signature; drop old versions first.

DO $$
DECLARE
  r RECORD;
BEGIN
  FOR r IN
    SELECT oid::regprocedure::text AS proc
    FROM pg_proc
    WHERE proname = 'sp_student_admit'
      AND pronamespace = 'public'::regnamespace
  LOOP
    EXECUTE 'DROP PROCEDURE IF EXISTS ' || r.proc;
  END LOOP;
END $$;

DO $$
DECLARE
  r RECORD;
BEGIN
  FOR r IN
    SELECT oid::regprocedure::text AS proc
    FROM pg_proc
    WHERE proname = 'fn_student_admit'
      AND pronamespace = 'public'::regnamespace
  LOOP
    EXECUTE 'DROP FUNCTION IF EXISTS ' || r.proc;
  END LOOP;
END $$;

-- Apply definitions from repository: run src/db/stored_procedures_students.sql (at least CREATE PROCEDURE sp_student_admit and CREATE FUNCTION fn_student_admit).
