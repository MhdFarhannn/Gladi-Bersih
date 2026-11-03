#!/usr/bin/env bash

set -uo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5257}"
ADMIN_USERNAME="${ADMIN_USERNAME:-endpoint_admin}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-EndpointTest!42}"

work_dir="$(mktemp -d)"
body_file="$work_dir/body"
header_file="$work_dir/headers"
swagger_file="$work_dir/swagger.json"
documented_file="$work_dir/documented-endpoints"
exercised_file="$work_dir/exercised-endpoints"
coverage_diff_file="$work_dir/coverage.diff"
trap 'rm -rf "$work_dir"' EXIT

declare -A covered=()
request_count=0
request_pass_count=0
assertion_count=0
assertion_pass_count=0
failures='[]'
last_status=""

add_failure() {
    local message="$1"
    failures="$(jq -c --arg message "$message" '. + [$message]' <<<"$failures")"
}

request() {
    local name="$1"
    local expected_statuses="$2"
    local method="$3"
    local actual_path="$4"
    local route_template="$5"
    local token="${6-}"
    local body="${7-__NO_BODY__}"
    local response_summary
    local -a curl_args=(
        --silent
        --show-error
        --connect-timeout 5
        --max-time 30
        --output "$body_file"
        --dump-header "$header_file"
        --write-out "%{http_code}"
        --request "$method"
        --header "Accept: application/json"
    )

    if [[ -n "$token" ]]; then
        curl_args+=(--header "Authorization: Bearer $token")
    fi

    if [[ "$body" != "__NO_BODY__" ]]; then
        curl_args+=(--header "Content-Type: application/json" --data "$body")
    fi

    : >"$body_file"
    : >"$header_file"
    last_status="$(curl "${curl_args[@]}" "$BASE_URL$actual_path")"
    ((request_count += 1))
    covered["$method $route_template"]=1

    if [[ " $expected_statuses " == *" $last_status "* ]]; then
        ((request_pass_count += 1))
        return
    fi

    response_summary="$(jq -c . "$body_file" 2>/dev/null || tr '\n' ' ' <"$body_file")"
    add_failure "$name: expected HTTP $expected_statuses, received $last_status; body=${response_summary:0:500}"
}

multipart_request() {
    local name="$1"
    local expected_statuses="$2"
    local method="$3"
    local actual_path="$4"
    local route_template="$5"
    local token="$6"
    local response_summary
    shift 6
    local -a curl_args=(
        --silent
        --show-error
        --connect-timeout 5
        --max-time 30
        --output "$body_file"
        --dump-header "$header_file"
        --write-out "%{http_code}"
        --request "$method"
        --header "Accept: application/json"
    )

    if [[ -n "$token" ]]; then
        curl_args+=(--header "Authorization: Bearer $token")
    fi

    local form_field
    for form_field in "$@"; do
        curl_args+=(--form "$form_field")
    done

    : >"$body_file"
    : >"$header_file"
    last_status="$(curl "${curl_args[@]}" "$BASE_URL$actual_path")"
    ((request_count += 1))
    covered["$method $route_template"]=1

    if [[ " $expected_statuses " == *" $last_status "* ]]; then
        ((request_pass_count += 1))
        return
    fi

    response_summary="$(jq -c . "$body_file" 2>/dev/null || tr '\n' ' ' <"$body_file")"
    add_failure "$name: expected HTTP $expected_statuses, received $last_status; body=${response_summary:0:500}"
}

extract_json() {
    local label="$1"
    local filter="$2"
    local value

    if value="$(jq -er "$filter" "$body_file" 2>/dev/null)"; then
        command printf '%s' "$value"
        return
    fi

    add_failure "$label: response did not contain $filter"
    command printf ''
}

assert_json() {
    local label="$1"
    shift
    local filter="${*: -1}"
    ((assertion_count += 1))

    if jq -e "$@" "$body_file" >/dev/null 2>&1; then
        ((assertion_pass_count += 1))
    else
        add_failure "$label: JSON assertion failed ($filter)"
    fi
}

assert_body_contains() {
    local label="$1"
    local expected="$2"
    local body
    ((assertion_count += 1))
    body="$(<"$body_file")"

    if [[ "$body" == *"$expected"* ]]; then
        ((assertion_pass_count += 1))
    else
        add_failure "$label: response body did not contain '$expected'"
    fi
}

assert_header_contains() {
    local label="$1"
    local expected="${2,,}"
    local headers
    ((assertion_count += 1))
    headers="$(<"$header_file")"

    if [[ "${headers,,}" == *"$expected"* ]]; then
        ((assertion_pass_count += 1))
    else
        add_failure "$label: response headers did not contain '$expected'"
    fi
}

assert_url_status() {
    local label="$1"
    local path="$2"
    local expected_status="$3"
    local actual_status
    ((assertion_count += 1))
    actual_status="$(curl \
        --silent \
        --show-error \
        --connect-timeout 5 \
        --max-time 30 \
        --output /dev/null \
        --write-out "%{http_code}" \
        "$BASE_URL/$path")"

    if [[ "$actual_status" == "$expected_status" ]]; then
        ((assertion_pass_count += 1))
    else
        add_failure "$label: expected HTTP $expected_status, received $actual_status"
    fi
}

if ! curl \
    --fail \
    --silent \
    --show-error \
    --connect-timeout 5 \
    --max-time 30 \
    "$BASE_URL/swagger/v1/swagger.json" \
    --output "$swagger_file"; then
    jq -n --arg baseUrl "$BASE_URL" '{baseUrl: $baseUrl, error: "Swagger is unavailable. Run the API in Development."}'
    exit 2
fi

((assertion_count += 1))
if jq -e '
    [.paths["/api/alat"].post, .paths["/api/alat/{id}"].put]
    | all(.[].requestBody.content;
        has("multipart/form-data")
        and (has("application/json") | not)
        and (."multipart/form-data".schema.properties.image.format == "binary")
        and (."multipart/form-data".schema.properties | has("idKategori"))
        and (."multipart/form-data".schema.properties | has("nama"))
        and (."multipart/form-data".schema.properties | has("deskripsi"))
        and (."multipart/form-data".schema.properties | has("hargaSewa")))
' "$swagger_file" >/dev/null; then
    ((assertion_pass_count += 1))
else
    add_failure "alat Swagger schema: multipart upload fields were not rendered correctly"
fi

suffix="$(date +%s%N)"
suffix="${suffix: -14}"
borrower_username="borrower_$suffix"
officer_username="officer_$suffix"
borrower_phone="081${suffix: -9}"
officer_phone="082${suffix: -9}"
category_name="Category_$suffix"
tool_name="Tool_$suffix"
inventory_code="INV-$suffix-A"
spare_inventory_code="INV-$suffix-B"
password="EndpointTest!42"
loan_date="$(date +%F)"
planned_date="$(date -d '+1 day' +%F)"
updated_planned_date="$(date -d '+2 days' +%F)"
report_from="$(date -d '-1 day' +%F)"
report_to="$(date -d '+3 days' +%F)"
valid_image_file="$work_dir/valid.png"
replacement_image_file="$work_dir/replacement.png"
invalid_image_file="$work_dir/not-an-image.png"
oversized_image_file="$work_dir/oversized.png"

base64 --decode >"$valid_image_file" <<'IMAGE'
iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=
IMAGE
cp "$valid_image_file" "$replacement_image_file"
command printf 'this is not an image' >"$invalid_image_file"
cp "$valid_image_file" "$oversized_image_file"
truncate --size=2097153 "$oversized_image_file"

bootstrap_body="$(jq -nc \
    --arg username "$ADMIN_USERNAME" \
    --arg password "$ADMIN_PASSWORD" \
    '{username: $username, nama: "Endpoint Test Admin", password: $password, noTelepon: "080000000001", alamat: "Local test"}')"
request "bootstrap admin" "201 409" POST "/api/bootstrap/admin" "/api/bootstrap/admin" "" "$bootstrap_body"

admin_login_body="$(jq -nc \
    --arg username "$ADMIN_USERNAME" \
    --arg password "$ADMIN_PASSWORD" \
    '{username: $username, password: $password}')"
request "admin login" "200" POST "/api/auth/login" "/api/auth/login" "" "$admin_login_body"
admin_token="$(extract_json "admin login" '.data.token')"
assert_json "admin login role" '.success == true and .data.user.role == "Admin"'

invalid_login_body='{"username":"missing-user","password":"not-the-password"}'
request "invalid login" "401" POST "/api/auth/login" "/api/auth/login" "" "$invalid_login_body"
assert_json "invalid login envelope" '.success == false and .data == null'

request "register validation" "400" POST "/api/auth/register" "/api/auth/register" "" '{}'
assert_json "register validation envelope" '.success == false and (.message | length > 0)'

borrower_body="$(jq -nc \
    --arg username "$borrower_username" \
    --arg password "$password" \
    --arg phone "$borrower_phone" \
    '{username: $username, nama: "Endpoint Test Borrower", password: $password, noTelepon: $phone, alamat: "Local test"}')"
request "register borrower" "201" POST "/api/auth/register" "/api/auth/register" "" "$borrower_body"
borrower_id="$(extract_json "register borrower" '.data.id')"
assert_json "registered borrower role" '.success == true and .data.role == "Peminjam"'

borrower_login_body="$(jq -nc \
    --arg username "$borrower_username" \
    --arg password "$password" \
    '{username: $username, password: $password}')"
request "borrower login" "200" POST "/api/auth/login" "/api/auth/login" "" "$borrower_login_body"
borrower_token="$(extract_json "borrower login" '.data.token')"

request "users require authentication" "401" GET "/api/users" "/api/users"
assert_json "authentication challenge envelope" '.success == false and .data == null'
request "users reject borrower role" "403" GET "/api/users" "/api/users" "$borrower_token"
assert_json "authorization failure envelope" '.success == false and .data == null'

officer_create_body="$(jq -nc \
    --arg username "$officer_username" \
    --arg password "$password" \
    --arg phone "$officer_phone" \
    '{username: $username, nama: "Endpoint Test Officer", password: $password, noTelepon: $phone, alamat: "Local test", role: "Petugas"}')"
request "create officer" "201" POST "/api/users" "/api/users" "$admin_token" "$officer_create_body"
officer_id="$(extract_json "create officer" '.data.id')"
assert_json "created officer role" '.success == true and .data.role == "Petugas"'

request "list users" "200" GET "/api/users?page=1&pageSize=20&search=Endpoint" "/api/users" "$admin_token"
assert_json "user list pagination" '.success == true and (.data.items | type == "array")'
request "get officer" "200" GET "/api/users/$officer_id" "/api/users/{id}" "$admin_token"
assert_json "officer lookup" --argjson id "$officer_id" '.success == true and .data.id == $id'

officer_update_body="$(jq -nc \
    --arg username "$officer_username" \
    --arg phone "$officer_phone" \
    '{username: $username, nama: "Endpoint Test Officer Updated", noTelepon: $phone, alamat: "Updated local test", role: "Petugas"}')"
request "update officer" "200" PUT "/api/users/$officer_id" "/api/users/{id}" "$admin_token" "$officer_update_body"
assert_json "updated officer" '.success == true and .data.nama == "Endpoint Test Officer Updated"'

officer_login_body="$(jq -nc \
    --arg username "$officer_username" \
    --arg password "$password" \
    '{username: $username, password: $password}')"
request "officer login" "200" POST "/api/auth/login" "/api/auth/login" "" "$officer_login_body"
officer_token="$(extract_json "officer login" '.data.token')"

category_body="$(jq -nc --arg name "$category_name" '{nama: $name}')"
request "create category" "201" POST "/api/kategori" "/api/kategori" "$admin_token" "$category_body"
category_id="$(extract_json "create category" '.data.id')"
request "list categories" "200" GET "/api/kategori?page=1&pageSize=20&search=$category_name" "/api/kategori" "$borrower_token"
assert_json "category list" '.success == true and (.data.items | length) >= 1'
request "get category" "200" GET "/api/kategori/$category_id" "/api/kategori/{id}" "$borrower_token"

updated_category_name="${category_name}_Updated"
category_update_body="$(jq -nc --arg name "$updated_category_name" '{nama: $name}')"
request "update category" "200" PUT "/api/kategori/$category_id" "/api/kategori/{id}" "$admin_token" "$category_update_body"
assert_json "updated category" --arg name "$updated_category_name" '.success == true and .data.nama == $name'
request "officer cannot create category" "403" POST "/api/kategori" "/api/kategori" "$officer_token" "$category_body"

multipart_request "create tool with image" "201" POST "/api/alat" "/api/alat" "$admin_token" \
    "idKategori=$category_id" \
    "nama=$tool_name" \
    "deskripsi=Endpoint test equipment" \
    "hargaSewa=12500.50" \
    "image=@$valid_image_file;type=image/png"
tool_id="$(extract_json "create tool" '.data.id')"
original_image_path="$(extract_json "create tool image path" '.data.imagePath')"
assert_json "created tool image path" '.success == true and (.data.imagePath | test("^uploads/alat/[0-9a-f]{32}\\.png$"))'
assert_url_status "created tool image is served" "$original_image_path" "200"

tool_without_image_name="${tool_name}_NoImage"
multipart_request "create tool without image" "201" POST "/api/alat" "/api/alat" "$admin_token" \
    "idKategori=$category_id" \
    "nama=$tool_without_image_name" \
    "deskripsi=Endpoint test equipment without image" \
    "hargaSewa=10000"
tool_without_image_id="$(extract_json "create tool without image" '.data.id')"
assert_json "created tool without image" '.success == true and .data.imagePath == null'

multipart_request "reject non-image upload" "400" POST "/api/alat" "/api/alat" "$admin_token" \
    "idKategori=$category_id" \
    "nama=${tool_name}_InvalidImage" \
    "deskripsi=Invalid image test" \
    "hargaSewa=10000" \
    "image=@$invalid_image_file;type=image/png"
assert_json "non-image rejection" '.success == false and (.message | contains("berisi gambar yang valid"))'

multipart_request "reject oversized image" "400" POST "/api/alat" "/api/alat" "$admin_token" \
    "idKategori=$category_id" \
    "nama=${tool_name}_OversizedImage" \
    "deskripsi=Oversized image test" \
    "hargaSewa=10000" \
    "image=@$oversized_image_file;type=image/png"
assert_json "oversized image rejection" '.success == false and (.message | contains("maksimal 2 MB"))'

request "list tools" "200" GET "/api/alat?page=1&pageSize=20&search=$tool_name" "/api/alat" "$borrower_token"
assert_json "tool list" '.success == true and (.data.items | length) >= 1'
request "get tool" "200" GET "/api/alat/$tool_id" "/api/alat/{id}" "$borrower_token"
assert_json "tool detail relative image path" --arg imagePath "$original_image_path" '.success == true and .data.imagePath == $imagePath'

updated_tool_name="${tool_name}_Updated"
multipart_request "update tool without image" "200" PUT "/api/alat/$tool_id" "/api/alat/{id}" "$admin_token" \
    "idKategori=$category_id" \
    "nama=$updated_tool_name" \
    "deskripsi=Updated endpoint test equipment" \
    "hargaSewa=15000"
assert_json "updated tool keeps image" \
    --arg name "$updated_tool_name" \
    --arg imagePath "$original_image_path" \
    '.success == true and .data.nama == $name and .data.imagePath == $imagePath'
assert_url_status "preserved tool image is served" "$original_image_path" "200"

multipart_request "update tool with replacement image" "200" PUT "/api/alat/$tool_id" "/api/alat/{id}" "$admin_token" \
    "idKategori=$category_id" \
    "nama=$updated_tool_name" \
    "deskripsi=Updated endpoint test equipment" \
    "hargaSewa=15000" \
    "image=@$replacement_image_file;type=image/png"
replacement_image_path="$(extract_json "replacement image path" '.data.imagePath')"
assert_json "updated tool replaces image" \
    --arg oldImagePath "$original_image_path" \
    '.success == true and (.data.imagePath | test("^uploads/alat/[0-9a-f]{32}\\.png$")) and .data.imagePath != $oldImagePath'
assert_url_status "old tool image was removed" "$original_image_path" "404"
assert_url_status "replacement tool image is served" "$replacement_image_path" "200"

request "list updated tools" "200" GET "/api/alat?page=1&pageSize=20&search=$updated_tool_name" "/api/alat" "$borrower_token"
assert_json "tool list relative image path" \
    --argjson id "$tool_id" \
    --arg imagePath "$replacement_image_path" \
    '.success == true and any(.data.items[]; .id == $id and .imagePath == $imagePath)'
request "get updated tool" "200" GET "/api/alat/$tool_id" "/api/alat/{id}" "$borrower_token"
assert_json "updated tool detail relative image path" --arg imagePath "$replacement_image_path" '.success == true and .data.imagePath == $imagePath'

unit_body="$(jq -nc \
    --argjson toolId "$tool_id" \
    --arg code "$inventory_code" \
    '{idAlat: $toolId, kodeInventaris: $code, statusKetersediaan: "tersedia", statusBarang: "baik"}')"
request "create unit" "201" POST "/api/unit-alat" "/api/unit-alat" "$admin_token" "$unit_body"
unit_id="$(extract_json "create unit" '.data.id')"

spare_unit_body="$(jq -nc \
    --argjson toolId "$tool_id" \
    --arg code "$spare_inventory_code" \
    '{idAlat: $toolId, kodeInventaris: $code, statusKetersediaan: "tersedia", statusBarang: "baik"}')"
request "create spare unit" "201" POST "/api/unit-alat" "/api/unit-alat" "$admin_token" "$spare_unit_body"
spare_unit_id="$(extract_json "create spare unit" '.data.id')"

request "list units" "200" GET "/api/unit-alat?page=1&pageSize=20&search=INV-$suffix" "/api/unit-alat" "$officer_token"
assert_json "unit list" '.success == true and (.data.items | length) >= 2'
request "get unit" "200" GET "/api/unit-alat/$unit_id" "/api/unit-alat/{id}" "$officer_token"

unit_update_body="$(jq -nc \
    --argjson toolId "$tool_id" \
    --arg code "$inventory_code" \
    '{idAlat: $toolId, kodeInventaris: $code, statusKetersediaan: "tersedia", statusBarang: "baik"}')"
request "update unit" "200" PUT "/api/unit-alat/$unit_id" "/api/unit-alat/{id}" "$admin_token" "$unit_update_body"

loan_body="$(jq -nc \
    --arg loanDate "$loan_date" \
    --arg plannedDate "$planned_date" \
    --argjson toolId "$tool_id" \
    '{tanggalPeminjaman: $loanDate, tanggalRencanaKembali: $plannedDate, items: [{idAlat: $toolId, jumlah: 1}]}')"
request "create loan to reject" "201" POST "/api/peminjaman" "/api/peminjaman" "$borrower_token" "$loan_body"
rejected_loan_id="$(extract_json "create loan to reject" '.data.id')"

reject_body='{"alasan":"Endpoint rejection branch"}'
request "reject loan" "200" POST "/api/peminjaman/$rejected_loan_id/tolak" "/api/peminjaman/{id}/tolak" "$officer_token" "$reject_body"
assert_json "rejected loan status" '.success == true and .data.status == "ditolak"'
request "delete rejected loan" "200" DELETE "/api/peminjaman/$rejected_loan_id" "/api/peminjaman/{id}" "$admin_token"

request "create main loan" "201" POST "/api/peminjaman" "/api/peminjaman" "$borrower_token" "$loan_body"
loan_id="$(extract_json "create main loan" '.data.id')"
request "list loans" "200" GET "/api/peminjaman?page=1&pageSize=20&status=diajukan" "/api/peminjaman" "$borrower_token"
assert_json "loan list ownership" --argjson borrowerId "$borrower_id" '.success == true and all(.data.items[]; .idPeminjam == $borrowerId)'
request "get loan" "200" GET "/api/peminjaman/$loan_id" "/api/peminjaman/{id}" "$borrower_token"

loan_update_body="$(jq -nc \
    --arg loanDate "$loan_date" \
    --arg plannedDate "$updated_planned_date" \
    --argjson toolId "$tool_id" \
    '{tanggalPeminjaman: $loanDate, tanggalRencanaKembali: $plannedDate, items: [{idAlat: $toolId, jumlah: 1}]}')"
request "update loan" "200" PUT "/api/peminjaman/$loan_id" "/api/peminjaman/{id}" "$borrower_token" "$loan_update_body"
assert_json "updated loan date" --arg plannedDate "$updated_planned_date" '.success == true and .data.tanggalRencanaKembali == $plannedDate'

request "admin cannot approve loan" "403" POST "/api/peminjaman/$loan_id/setujui" "/api/peminjaman/{id}/setujui" "$admin_token"
request "approve loan" "200" POST "/api/peminjaman/$loan_id/setujui" "/api/peminjaman/{id}/setujui" "$officer_token"
allocated_unit_id="$(extract_json "approve loan" '.data.items[0].unit[0].idUnitAlat')"
assert_json "approved loan allocation" '.success == true and .data.status == "disetujui" and (.data.items[0].unit | length) == 1'

request "hand over loan" "200" POST "/api/peminjaman/$loan_id/serahkan" "/api/peminjaman/{id}/serahkan" "$officer_token"
assert_json "handed-over loan status" '.success == true and .data.status == "dipinjam"'

return_body="$(jq -nc \
    --argjson loanId "$loan_id" \
    --arg returnDate "$loan_date" \
    --argjson unitId "$allocated_unit_id" \
    '{idPeminjaman: $loanId, tanggalKembali: $returnDate, catatan: "Endpoint return", items: [{idUnitAlat: $unitId, statusKondisi: "baik", catatan: "Initial inspection"}]}')"
request "create return" "201" POST "/api/pengembalian" "/api/pengembalian" "$borrower_token" "$return_body"
return_id="$(extract_json "create return" '.data.id')"
assert_json "created return status" '.success == true and .data.statusPeminjaman == "selesai"'

request "list returns" "200" GET "/api/pengembalian?page=1&pageSize=20&search=$borrower_username" "/api/pengembalian" "$borrower_token"
assert_json "return list ownership" --argjson borrowerId "$borrower_id" '.success == true and all(.data.items[]; .idPeminjam == $borrowerId)'
request "get return" "200" GET "/api/pengembalian/$return_id" "/api/pengembalian/{id}" "$borrower_token"

verify_body="$(jq -nc \
    --arg returnDate "$loan_date" \
    --argjson unitId "$allocated_unit_id" \
    '{tanggalKembali: $returnDate, catatan: "Verified endpoint return", items: [{idUnitAlat: $unitId, statusKondisi: "rusak_ringan", catatan: "Verified light damage"}]}')"
request "verify return" "200" PUT "/api/pengembalian/$return_id/verifikasi" "/api/pengembalian/{id}/verifikasi" "$officer_token" "$verify_body"
assert_json "verified return condition" '.success == true and .data.items[0].statusKondisi == "rusak_ringan" and .data.items[0].dendaKondisi > 0'

report_query="from=$report_from&to=$report_to"
request "loan report" "200" GET "/api/laporan/peminjaman?$report_query" "/api/laporan/peminjaman" "$officer_token"
assert_json "loan report data" --argjson loanId "$loan_id" '.success == true and any(.data.items[]; .idPeminjaman == $loanId)'

request "loan CSV report" "200" GET "/api/laporan/peminjaman/csv?$report_query" "/api/laporan/peminjaman/csv" "$officer_token"
assert_header_contains "loan CSV content type" "content-type: text/csv"
assert_body_contains "loan CSV header" "ID Peminjaman,Tanggal Pinjam"

request "return report" "200" GET "/api/laporan/pengembalian?$report_query" "/api/laporan/pengembalian" "$admin_token"
assert_json "return report data" --argjson returnId "$return_id" '.success == true and any(.data.items[]; .idPengembalian == $returnId)'

request "return CSV report" "200" GET "/api/laporan/pengembalian/csv?$report_query" "/api/laporan/pengembalian/csv" "$admin_token"
assert_header_contains "return CSV content type" "content-type: text/csv"
assert_body_contains "return CSV header" "ID Pengembalian,ID Peminjaman"

request "activity log" "200" GET "/api/log-aktivitas?page=1&pageSize=100&from=$report_from&to=$report_to" "/api/log-aktivitas" "$admin_token"
assert_json "activity log data" '.success == true and (.data.items | length) > 0'

request "borrower logout" "200" POST "/api/auth/logout" "/api/auth/logout" "$borrower_token"
request "delete return" "200" DELETE "/api/pengembalian/$return_id" "/api/pengembalian/{id}" "$admin_token"
request "delete completed loan" "200" DELETE "/api/peminjaman/$loan_id" "/api/peminjaman/{id}" "$admin_token"
request "delete allocated unit" "200" DELETE "/api/unit-alat/$unit_id" "/api/unit-alat/{id}" "$admin_token"
request "delete spare unit" "200" DELETE "/api/unit-alat/$spare_unit_id" "/api/unit-alat/{id}" "$admin_token"
request "delete tool" "200" DELETE "/api/alat/$tool_id" "/api/alat/{id}" "$admin_token"
assert_url_status "soft-deleted tool image remains" "$replacement_image_path" "200"
request "delete tool without image" "200" DELETE "/api/alat/$tool_without_image_id" "/api/alat/{id}" "$admin_token"
request "delete category" "200" DELETE "/api/kategori/$category_id" "/api/kategori/{id}" "$admin_token"
request "delete borrower" "200" DELETE "/api/users/$borrower_id" "/api/users/{id}" "$admin_token"
request "delete officer" "200" DELETE "/api/users/$officer_id" "/api/users/{id}" "$admin_token"

jq -r '
    .paths
    | to_entries[] as $path
    | $path.value
    | to_entries[]
    | select(.key == "get" or .key == "post" or .key == "put" or .key == "delete")
    | "\(.key | ascii_upcase) \($path.key)"
' "$swagger_file" | LC_ALL=C sort >"$documented_file"

for endpoint in "${!covered[@]}"; do
    command printf '%s\n' "$endpoint"
done | LC_ALL=C sort >"$exercised_file"

((assertion_count += 1))
if diff -u "$documented_file" "$exercised_file" >"$coverage_diff_file"; then
    ((assertion_pass_count += 1))
else
    coverage_diff="$(tr '\n' ' ' <"$coverage_diff_file")"
    add_failure "Swagger coverage mismatch: ${coverage_diff:0:1000}"
fi

documented_count="$(wc -l <"$documented_file")"
covered_count="$(wc -l <"$exercised_file")"
failure_count="$(jq 'length' <<<"$failures")"

jq -n \
    --arg baseUrl "$BASE_URL" \
    --argjson documentedOperations "$documented_count" \
    --argjson coveredOperations "$covered_count" \
    --argjson requests "$request_count" \
    --argjson passedRequests "$request_pass_count" \
    --argjson assertions "$assertion_count" \
    --argjson passedAssertions "$assertion_pass_count" \
    --argjson failures "$failures" \
    '{
        baseUrl: $baseUrl,
        documentedOperations: $documentedOperations,
        coveredOperations: $coveredOperations,
        requests: $requests,
        passedRequests: $passedRequests,
        assertions: $assertions,
        passedAssertions: $passedAssertions,
        failures: $failures
    }'

if ((failure_count > 0)); then
    exit 1
fi
