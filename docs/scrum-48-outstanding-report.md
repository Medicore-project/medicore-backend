# SCRUM-48: Outstanding invoice report

Admins can open **Reports → Outstanding Invoices** or call `GET /billing/reports/outstanding` through the gateway. The response is JSON by default; `format=Csv` and `format=Pdf` download the same filtered report.

The report includes only invoices in `Payable` status whose `Total` exceeds `AmountPaid`. Draft, void and fully paid invoices are excluded. The balance is `Total - AmountPaid`, so partial payments reduce the reported amount. Currency totals remain separate.

Age is the number of **Asia/Colombo calendar days since `FinalizedAtUtc`**, when the invoice became payable. The buckets are 0–30, 31–60, 61–90 and 91+ days. The response and exports show the date used for ageing. An invoice with a future finalization timestamp is excluded.

Optional `departmentId`, `currency`, `from` and `to` query parameters filter the result. `from` and `to` are inclusive **invoice issue dates** in Asia/Colombo. With no dates, the report includes outstanding invoices from all issue dates; it does not default to the current month, because old debt would disappear. If both dates are supplied, the range is limited to 366 days. `departmentName` is an optional display label for exports and never enters SQL. All query values are passed through typed SQL parameters.

The page displays totals by currency, an ageing chart, a breakdown by department and bucket, and invoice links. CSV and PDF contain the same totals, breakdown and invoice details. No schema change or migration is required.
