ALTER TABLE leave_requests
ADD COLUMN IF NOT EXISTS is_half_day BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE leave_requests
ADD COLUMN IF NOT EXISTS half_day_session VARCHAR(20) NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_leave_requests_half_day_session'
    ) THEN
        ALTER TABLE leave_requests
        ADD CONSTRAINT ck_leave_requests_half_day_session
        CHECK (
            half_day_session IS NULL
            OR half_day_session IN ('FirstHalf', 'SecondHalf')
        );
    END IF;
END $$;
