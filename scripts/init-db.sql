-- MediCore database initialisation
-- Runs automatically on first `docker compose up`

-- ── Service schemas ──────────────────────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS medicore_identity;
CREATE SCHEMA IF NOT EXISTS medicore_patient;
CREATE SCHEMA IF NOT EXISTS medicore_appointment;
CREATE SCHEMA IF NOT EXISTS medicore_billing;

-- ── Test schema (used by xUnit integration tests only) ───────────────────────
CREATE SCHEMA IF NOT EXISTS medicore_test;

-- ── Per-schema login roles (no cross-schema grants) ──────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'identity_svc') THEN
        CREATE ROLE identity_svc LOGIN PASSWORD 'identity_dev';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'patient_svc') THEN
        CREATE ROLE patient_svc LOGIN PASSWORD 'patient_dev';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'appointment_svc') THEN
        CREATE ROLE appointment_svc LOGIN PASSWORD 'appointment_dev';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'billing_svc') THEN
        CREATE ROLE billing_svc LOGIN PASSWORD 'billing_dev';
    END IF;
END
$$;

GRANT ALL ON SCHEMA medicore_identity    TO identity_svc;
GRANT ALL ON SCHEMA medicore_patient     TO patient_svc;
GRANT ALL ON SCHEMA medicore_appointment TO appointment_svc;
GRANT ALL ON SCHEMA medicore_billing     TO billing_svc;
GRANT ALL ON SCHEMA medicore_test        TO identity_svc;
