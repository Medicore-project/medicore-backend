[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:5000",
    [string]$AdminEmail = "admin@medicore.local",
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword
)

$ErrorActionPreference = "Stop"

function Invoke-MediCoreApi {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Token,
        [object]$Body
    )

    $request = @{
        Method      = $Method
        Uri         = "$GatewayUrl$Path"
        ErrorAction = "Stop"
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $request.Headers = @{ Authorization = "Bearer $Token" }
    }

    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }

    Invoke-RestMethod @request
}

function Get-OrCreateNamedRecord {
    param(
        [Parameter(Mandatory = $true)][string]$CollectionPath,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string]$Token
    )

    $records = @(Invoke-MediCoreApi -Method Get -Path $CollectionPath -Token $Token)
    $record = $records | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if ($null -eq $record) {
        $record = Invoke-MediCoreApi -Method Post -Path $CollectionPath -Token $Token -Body @{
            name = $Name
            description = $Description
        }
        Write-Host "Created $Name"
    }
    else {
        Write-Host "Reused $Name"
    }

    $record
}

Write-Host "Signing in to $GatewayUrl as $AdminEmail..."
$login = Invoke-MediCoreApi -Method Post -Path "/auth/login" -Body @{
    email = $AdminEmail
    password = $AdminPassword
}
$adminToken = $login.accessToken

if ([string]::IsNullOrWhiteSpace($adminToken)) {
    throw "Login succeeded without returning an access token."
}

$departmentSeeds = @(
    @{ Name = "General Medicine"; Description = "Primary and general medical care" },
    @{ Name = "Cardiology"; Description = "Heart and cardiovascular care" },
    @{ Name = "Pediatrics"; Description = "Medical care for infants, children and adolescents" },
    @{ Name = "Outpatient Services"; Description = "Registration and ambulatory patient services" }
)

$departmentsByName = @{}
foreach ($seed in $departmentSeeds) {
    $record = Get-OrCreateNamedRecord -CollectionPath "/api/departments" -Name $seed.Name -Description $seed.Description -Token $adminToken
    $departmentsByName[$seed.Name] = $record
}

$specializationSeeds = @(
    @{ Name = "General Practice"; Description = "General consultations and primary care" },
    @{ Name = "Cardiology"; Description = "Diagnosis and treatment of heart conditions" },
    @{ Name = "Pediatrics"; Description = "Child and adolescent medicine" },
    @{ Name = "Dermatology"; Description = "Skin, hair and nail care" },
    @{ Name = "Orthopedics"; Description = "Musculoskeletal care" }
)

foreach ($seed in $specializationSeeds) {
    Get-OrCreateNamedRecord -CollectionPath "/api/specializations" -Name $seed.Name -Description $seed.Description -Token $adminToken | Out-Null
}

$demoPassword = "DemoUser@123"
$staffSeeds = @(
    @{
        Email = "amara.perera@medicore.local"; Role = "Doctor"; FirstName = "Amara"; LastName = "Perera"
        Phone = "0771001001"; Specialization = "General Practice"; Department = "General Medicine"
    },
    @{
        Email = "nimal.fernando@medicore.local"; Role = "Doctor"; FirstName = "Nimal"; LastName = "Fernando"
        Phone = "0771001002"; Specialization = "Cardiology"; Department = "Cardiology"
    },
    @{
        Email = "savindi.silva@medicore.local"; Role = "Doctor"; FirstName = "Savindi"; LastName = "Silva"
        Phone = "0771001003"; Specialization = "Pediatrics"; Department = "Pediatrics"
    },
    @{
        Email = "maya.jayasinghe@medicore.local"; Role = "Nurse"; FirstName = "Maya"; LastName = "Jayasinghe"
        Phone = "0771001004"; Specialization = ""; Department = "General Medicine"
    },
    @{
        Email = "reception@medicore.local"; Role = "Receptionist"; FirstName = "Kasun"; LastName = "Wijesinghe"
        Phone = "0771001005"; Specialization = ""; Department = "Outpatient Services"
    }
)

$staffPage = Invoke-MediCoreApi -Method Get -Path "/api/staff?page=1&pageSize=100" -Token $adminToken
$staff = @($staffPage.items)
$staffByEmail = @{}
foreach ($member in $staff) {
    $staffByEmail[$member.email.ToLowerInvariant()] = $member
}

foreach ($seed in $staffSeeds) {
    $key = $seed.Email.ToLowerInvariant()
    if (-not $staffByEmail.ContainsKey($key)) {
        $member = Invoke-MediCoreApi -Method Post -Path "/api/staff" -Token $adminToken -Body @{
            email = $seed.Email
            password = $demoPassword
            role = $seed.Role
            firstName = $seed.FirstName
            lastName = $seed.LastName
            phone = $seed.Phone
            specialization = $seed.Specialization
            departmentId = $departmentsByName[$seed.Department].id
        }
        $staffByEmail[$key] = $member
        Write-Host "Created $($seed.Role): $($seed.FirstName) $($seed.LastName)"
    }
    else {
        Write-Host "Reused staff: $($seed.Email)"
    }
}

# Re-announce doctors so a fresh Appointment service receives the complete directory.
Invoke-MediCoreApi -Method Post -Path "/api/staff/doctors/republish" -Token $adminToken -Body @{} | Out-Null

$patientSeeds = @(
    @{
        Nic = "199012345678"; FirstName = "Dinesh"; LastName = "Bandara"; DateOfBirth = "1990-05-14"; Gender = "Male"
        Email = "dinesh.bandara@example.test"; Phone = "0772001001"; AddressLine1 = "18 Lake Road"; AddressLine2 = ""
        District = "Colombo"; EmergencyContactName = "Nadeesha Bandara"; EmergencyContactPhone = "0773001001"
    },
    @{
        Nic = "198765432109"; FirstName = "Tharushi"; LastName = "Peris"; DateOfBirth = "1987-11-22"; Gender = "Female"
        Email = "tharushi.peris@example.test"; Phone = "0772001002"; AddressLine1 = "42 Temple Lane"; AddressLine2 = ""
        District = "Gampaha"; EmergencyContactName = "Ruwan Peris"; EmergencyContactPhone = "0773001002"
    },
    @{
        Nic = "200112345679"; FirstName = "Kavindu"; LastName = "Senanayake"; DateOfBirth = "2001-03-08"; Gender = "Male"
        Email = "kavindu.senanayake@example.test"; Phone = "0772001003"; AddressLine1 = "7 Hill Street"; AddressLine2 = ""
        District = "Kandy"; EmergencyContactName = "Malini Senanayake"; EmergencyContactPhone = "0773001003"
    },
    @{
        Nic = "199556789012"; FirstName = "Ishara"; LastName = "Gunawardena"; DateOfBirth = "1995-07-19"; Gender = "Female"
        Email = "ishara.gunawardena@example.test"; Phone = "0772001004"; AddressLine1 = "91 Beach Road"; AddressLine2 = ""
        District = "Galle"; EmergencyContactName = "Saman Gunawardena"; EmergencyContactPhone = "0773001004"
    },
    @{
        Nic = "201023456781"; FirstName = "Minoli"; LastName = "De Alwis"; DateOfBirth = "2010-09-03"; Gender = "Female"
        Email = "minoli.dealwis@example.test"; Phone = "0772001005"; AddressLine1 = "25 Station Road"; AddressLine2 = ""
        District = "Kalutara"; EmergencyContactName = "Anushka De Alwis"; EmergencyContactPhone = "0773001005"
    }
)

$patients = @()
foreach ($seed in $patientSeeds) {
    $encodedNic = [Uri]::EscapeDataString($seed.Nic)
    $search = Invoke-MediCoreApi -Method Get -Path "/patient/api/patients/search?q=$encodedNic&page=1&pageSize=10" -Token $adminToken
    $patient = @($search.items) | Where-Object { $_.nic -eq $seed.Nic } | Select-Object -First 1

    if ($null -eq $patient) {
        $patient = Invoke-MediCoreApi -Method Post -Path "/patient/api/patients" -Token $adminToken -Body @{
            nic = $seed.Nic
            firstName = $seed.FirstName
            lastName = $seed.LastName
            dateOfBirth = $seed.DateOfBirth
            gender = $seed.Gender
            email = $seed.Email
            phone = $seed.Phone
            addressLine1 = $seed.AddressLine1
            addressLine2 = $seed.AddressLine2
            district = $seed.District
            emergencyContactName = $seed.EmergencyContactName
            emergencyContactPhone = $seed.EmergencyContactPhone
        }
        Write-Host "Created patient: $($patient.fullName) ($($patient.patientNumber))"
    }
    else {
        Write-Host "Reused patient: $($patient.fullName) ($($patient.patientNumber))"
    }

    $patients += [pscustomobject]@{
        PatientId = $patient.patientId
        PatientNumber = $patient.patientNumber
        FullName = $patient.fullName
        DateOfBirth = $seed.DateOfBirth
    }
}

$doctorEmails = @(
    "amara.perera@medicore.local",
    "nimal.fernando@medicore.local",
    "savindi.silva@medicore.local"
)
$doctors = @($doctorEmails | ForEach-Object { $staffByEmail[$_] })

# Kafka propagation is asynchronous. Wait until every seeded doctor is bookable.
$appointmentDoctors = @()
for ($attempt = 1; $attempt -le 30; $attempt++) {
    try {
        $appointmentDoctors = @(
            Invoke-MediCoreApi -Method Get -Path "/appointment/api/doctors" -Token $adminToken |
                Where-Object { $null -ne $_ -and $null -ne $_.doctorId }
        )
    }
    catch {
        $appointmentDoctors = @()
    }

    $knownIds = @($appointmentDoctors | ForEach-Object { $_.doctorId.ToString() })
    $missing = @($doctors | Where-Object { $knownIds -notcontains $_.id.ToString() })
    if ($missing.Count -eq 0) {
        break
    }

    Start-Sleep -Seconds 1
}

$knownDoctorIds = @($appointmentDoctors | ForEach-Object { $_.doctorId.ToString() })
$stillMissing = @($doctors | Where-Object { $knownDoctorIds -notcontains $_.id.ToString() })
if ($stillMissing.Count -gt 0) {
    throw "Doctors were created, but the Appointment service did not receive their Kafka events within 30 seconds."
}

$today = Get-Date -Format "yyyy-MM-dd"
$scheduleSeeds = @(
    @{ Doctor = $doctors[0]; Days = @(1, 3, 5); Start = "09:00:00"; End = "12:00:00" },
    @{ Doctor = $doctors[1]; Days = @(2, 4); Start = "13:00:00"; End = "16:00:00" },
    @{ Doctor = $doctors[2]; Days = @(1, 4); Start = "09:00:00"; End = "12:00:00" }
)

foreach ($seed in $scheduleSeeds) {
    $existingSchedules = @(Invoke-MediCoreApi -Method Get -Path "/appointment/api/schedules/doctor/$($seed.Doctor.id)" -Token $adminToken)
    foreach ($day in $seed.Days) {
        if ($null -eq ($existingSchedules | Where-Object { [int]$_.dayOfWeek -eq $day } | Select-Object -First 1)) {
            Invoke-MediCoreApi -Method Post -Path "/appointment/api/schedules" -Token $adminToken -Body @{
                doctorId = $seed.Doctor.id
                dayOfWeek = $day
                startTime = $seed.Start
                endTime = $seed.End
                slotDurationMinutes = 30
                effectiveFrom = $today
                effectiveTo = $null
            } | Out-Null
            Write-Host "Created schedule for $($seed.Doctor.fullName), weekday $day"
        }
    }
}

# Create three future bookings. Using patient booking tokens keeps names and patient numbers on
# the appointment events and gives SCRUM-42 realistic draft invoices.
$toDate = (Get-Date).AddDays(59).ToString("yyyy-MM-dd")
$existingAppointments = @(Invoke-MediCoreApi -Method Get -Path "/appointment/api/appointments?from=$today&to=$toDate" -Token $adminToken)
$serviceCodes = @("GEN-CONSULT", "SPEC-CONSULT", "FOLLOW-UP")
$bookings = @()

for ($index = 0; $index -lt 3; $index++) {
    $patient = $patients[$index]
    $doctor = $doctors[$index]
    $existing = $existingAppointments |
        Where-Object { $_.patientId.ToString() -eq $patient.PatientId.ToString() -and $_.status -ne "Cancelled" } |
        Select-Object -First 1

    if ($null -ne $existing) {
        $bookings += $existing
        Write-Host "Reused appointment for $($patient.FullName)"
        continue
    }

    $slots = @(Invoke-MediCoreApi -Method Get -Path "/appointment/api/slots/available?doctorId=$($doctor.id)&from=$today&to=$toDate" -Token $adminToken)
    $slot = $slots | Select-Object -First 1
    if ($null -eq $slot) {
        Write-Warning "No future slot was available for $($doctor.fullName); no sample appointment was created."
        continue
    }

    $bookingIdentity = Invoke-MediCoreApi -Method Post -Path "/patient/api/patients/identify" -Body @{
        patientNumber = $patient.PatientNumber
        dateOfBirth = $patient.DateOfBirth
    }

    $appointment = Invoke-MediCoreApi -Method Post -Path "/appointment/api/appointments" -Token $bookingIdentity.bookingToken -Body @{
        slotId = $slot.slotId
        patientId = $patient.PatientId
        serviceCode = $serviceCodes[$index]
    }
    $bookings += $appointment
    $existingAppointments += $appointment
    Write-Host "Booked $($serviceCodes[$index]) for $($patient.FullName)"
}

# Wait briefly for Billing to consume the appointment.booked events.
$invoiceCount = 0
foreach ($booking in $bookings) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $invoice = Invoke-MediCoreApi -Method Get -Path "/billing/api/invoices/by-appointment/$($booking.appointmentId)" -Token $adminToken
            if ($null -ne $invoice) {
                $invoiceCount++
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
}

Write-Host ""
Write-Host "Demo seed complete."
Write-Host "Departments: $($departmentSeeds.Count) ensured"
Write-Host "Specializations: $($specializationSeeds.Count) ensured"
Write-Host "Staff: $($staffSeeds.Count) ensured"
Write-Host "Patients: $($patients.Count) ensured"
Write-Host "Appointments: $($bookings.Count) available for testing"
Write-Host "Draft invoices observed: $invoiceCount"
Write-Host "Demo staff password: $demoPassword"
