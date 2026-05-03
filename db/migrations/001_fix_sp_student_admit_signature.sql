-- One-time fix: drop old sp_student_admit overloads so the new signature (13 params, timestamptz) exists.
-- Run this against your database first, then run stored_procedures_students.sql (or the sp_student_admit block).

-- Drop every known overload (IF EXISTS = no error if missing)
DROP PROCEDURE IF EXISTS public.sp_student_admit(uuid, text, text, text, uuid, uuid, uuid, timestamp with time zone, text, text, text, text, text, uuid);
DROP PROCEDURE IF EXISTS public.sp_student_admit(uuid, text, text, text, uuid, uuid, uuid, timestamp, text, text, text, text, text, uuid);
DROP PROCEDURE IF EXISTS public.sp_student_admit(uuid, text, text, text, uuid, uuid, timestamp, text, text, text, text, text, uuid);
DROP PROCEDURE IF EXISTS public.sp_student_admit(uuid, text, text, text, uuid, uuid, timestamp with time zone, text, text, text, text, text, uuid);

-- Then run: stored_procedures_students.sql (at least the CREATE OR REPLACE PROCEDURE sp_student_admit ... block).
