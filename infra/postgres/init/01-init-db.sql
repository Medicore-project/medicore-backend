-- =============================================================================
-- MediCore — PostgreSQL initialization script
-- Runs automatically on first `docker compose up` via docker-entrypoint-initdb.d
-- Idempotent: IF NOT EXISTS guards prevent errors on subsequent runs
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 1. Create service login roles (passwords match .env.example)
-- ---------------------------------------------------------------------------
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'identity_svc') THEN
    CREATE ROLE identity_svc WITH LOGIN PASSWORD 'identity_pass';
  END IF;

  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'patient_svc') THEN
    CREATE ROLE patient_svc WITH LOGIN PASSWORD 'patient_pass';
  END IF;

  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'appointment_svc') THEN
    CREATE ROLE appointment_svc WITH LOGIN PASSWORD 'appointment_pass';
  END IF;

  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'billing_svc') THEN
    CREATE ROLE billing_svc WITH LOGIN PASSWORD 'billing_pass';
  END IF;
END
$$;

-- ---------------------------------------------------------------------------
-- 2. Create schemas — one per service, strict ownership
-- ---------------------------------------------------------------------------

-- Identity service
CREATE SCHEMA IF NOT EXISTS medicore_identity AUTHORIZATION identity_svc;

-- Patient service
CREATE SCHEMA IF NOT EXISTS medicore_patient AUTHORIZATION patient_svc;

-- Appointment service
CREATE SCHEMA IF NOT EXISTS medicore_appointment AUTHORIZATION appointment_svc;

-- Billing service
CREATE SCHEMA IF NOT EXISTS medicore_billing AUTHORIZATION billing_svc;

-- Integration-test schema (reuses identity_svc role for simplicity)
CREATE SCHEMA IF NOT EXISTS medicore_test AUTHORIZATION identity_svc;

-- ---------------------------------------------------------------------------
-- 3. Grant schema-level privileges — each role can only see its own schema
-- ---------------------------------------------------------------------------

-- Identity
GRANT USAGE, CREATE ON SCHEMA medicore_identity TO identity_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_identity
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identity_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_identity
  GRANT USAGE, SELECT ON SEQUENCES TO identity_svc;

-- Patient
GRANT USAGE, CREATE ON SCHEMA medicore_patient TO patient_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_patient
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO patient_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_patient
  GRANT USAGE, SELECT ON SEQUENCES TO patient_svc;

-- Appointment
GRANT USAGE, CREATE ON SCHEMA medicore_appointment TO appointment_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_appointment
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO appointment_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_appointment
  GRANT USAGE, SELECT ON SEQUENCES TO appointment_svc;

-- Billing
GRANT USAGE, CREATE ON SCHEMA medicore_billing TO billing_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_billing
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO billing_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_billing
  GRANT USAGE, SELECT ON SEQUENCES TO billing_svc;

-- Test schema
GRANT USAGE, CREATE ON SCHEMA medicore_test TO identity_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_test
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identity_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA medicore_test
  GRANT USAGE, SELECT ON SEQUENCES TO identity_svc;

-- ---------------------------------------------------------------------------
-- 4. Revoke cross-schema access (defense-in-depth)
-- ---------------------------------------------------------------------------
-- Each role can only CONNECT to the shared medicore database, but has no
-- visibility into any schema it does not own.  EF Core migrations run as the
-- service role so they can never accidentally touch another service's tables.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Done — schemas visible via: \dn in psql
-- ---------------------------------------------------------------------------
