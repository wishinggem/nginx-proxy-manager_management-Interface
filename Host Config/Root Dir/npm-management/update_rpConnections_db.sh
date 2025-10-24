#!/bin/bash
export PATH="/usr/bin:/usr/local/bin:/opt/mssql-tools18/bin:$PATH"

INPUT_FILE="output.csv"
OUT_FILE="normalised.csv"
DATE_RANGE="-1d"
INFLUX_ORG="npmgrafstats"
INFLUX_TOKEN="{influx db token from the nginx proxy manager config}"
MSSQL_SERVER="{server ip};TrustServerCertificate=yes"
MSSQL_USER="{dbuser}"
MSSQL_PASSWORD="{dbpass}"
MSSQL_DATABASE="{db}"
MSSQL_TABLE="ReverseProxyConnections"


echo "--------------------"
echo "External Connections"
echo "--------------------"

echo "Starting Output Procces"
echo "Removing old $INPUT_FILE file"
sudo rm -rf $INPUT_FILE
echo "Removing old $OUT_FILE file"
sudo rm -rf $OUT_FILE

echo "Retrieving new data and saving to $INPUT_FILE"

QUERY="from(bucket: \"npmgrafstats\")
  |> range(start: ${DATE_RANGE})
  |> filter(fn: (r) => r._measurement == \"ReverseProxyConnections\")
  |> drop(columns: [\"Asn\", \"Domain\", \"IP\", \"Name\", \"Target\", \"City\", \"State\", \"latitude\", \"longitude\", \"key\"])
  |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")"
  
influx query "$QUERY" \
  --org "$INFLUX_ORG" \
  --token "$INFLUX_TOKEN" \
  --raw > $INPUT_FILE

echo "Formatting Data - Step 1: Remove first 3 rows and columns"
awk -F',' 'NR > 3 {for (i=4; i<=NF; i++) printf "%s%s", $i, (i<NF ? "," : "\n")}' $INPUT_FILE > temp.csv

echo "Formatting Data - Step 2: Remove _measurement column"
cut -d',' -f1-3,5- temp.csv > "$OUT_FILE"

echo "Cleaning up temporary file"
rm temp.csv

echo "Cleaning data for SQL import"
# Remove carriage returns and ensure Unix line endings
dos2unix "$OUT_FILE" 2>/dev/null || sed -i 's/\r$//' "$OUT_FILE"

# Ensure file ends with a newline
echo "" >> "$OUT_FILE"

echo "Uploading to MSSQL database"
bcp "${MSSQL_DATABASE}.dbo.${MSSQL_TABLE}" in "$OUT_FILE" \
  -S "$MSSQL_SERVER" \
  -U "$MSSQL_USER" \
  -P "$MSSQL_PASSWORD" \
  -C \
  -c \
  -t',' \
  -r'\n' \
  -F 2 \
  -b 100 \
  -e error.log \
  -m 0
  
echo "--------------------"
echo "Internal Connections"
echo "--------------------"

MSSQL_TABLE="InternalRProxyIPs"

echo "Starting Output Procces"
echo "Removing old $INPUT_FILE file"
sudo rm -rf $INPUT_FILE
echo "Removing old $OUT_FILE file"
sudo rm -rf $OUT_FILE

echo "Retrieving new data and saving to $INPUT_FILE"

QUERY="from(bucket: \"npmgrafstats\")
  |> range(start: ${DATE_RANGE})
  |> filter(fn: (r) => r._measurement == \"InternalRProxyIPs\")
  |> drop(columns: [\"Asn\", \"Domain\", \"IP\", \"Name\", \"Target\", \"City\", \"State\", \"latitude\", \"longitude\", \"key\"])
  |> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")"
  
influx query "$QUERY" \
  --org "$INFLUX_ORG" \
  --token "$INFLUX_TOKEN" \
  --raw > $INPUT_FILE

echo "Formatting Data - Step 1: Remove first 3 rows and columns"
awk -F',' 'NR > 3 {for (i=4; i<=NF; i++) printf "%s%s", $i, (i<NF ? "," : "\n")}' $INPUT_FILE > temp.csv

echo "Formatting Data - Step 2: Remove _measurement column"
cut -d',' -f1-3,5- temp.csv > "$OUT_FILE"

echo "Cleaning up temporary file"
rm temp.csv

echo "Cleaning data for SQL import"
# Remove carriage returns and ensure Unix line endings
dos2unix "$OUT_FILE" 2>/dev/null || sed -i 's/\r$//' "$OUT_FILE"

# Ensure file ends with a newline
echo "" >> "$OUT_FILE"

echo "Uploading to MSSQL database"
bcp "${MSSQL_DATABASE}.dbo.${MSSQL_TABLE}" in "$OUT_FILE" \
  -S "$MSSQL_SERVER" \
  -U "$MSSQL_USER" \
  -P "$MSSQL_PASSWORD" \
  -C \
  -c \
  -t',' \
  -r'\n' \
  -F 2 \
  -b 100 \
  -e error.log \
  -m 0
