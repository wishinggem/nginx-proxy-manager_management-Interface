#!/bin/bash
export PATH="/usr/bin:/usr/local/bin:/opt/mssql-tools18/bin:$PATH"

# Configuration
DB_HOST="{dbIP}"
DB_PORT="1433"
DB_NAME="{db}"
DB_USER="{dbUser}"
DB_PASS="{dbPass}"
HIT_THRESHOLD=800  # Number of hits within HOUR_TO_COUNT hour to trigger block
HOUR_TO_COUNT=1
CONF_FILE="/{Root Dir}/npm-management/host_data/ips.conf"
JSON_FILE="/{Root Dir}/npm-management/host_data/ips.json"
BACKUP_DIR="/{Root Dir}/npm-management/host_data/backups"
MALICIOUS_IP_THREADS=20
USE_DB_FOR_LOOKUP=true
DB_TABLE_NAME="{same table name as that is used in the web interface table name}"

# Function to get next available ID from database
get_next_id() {
    local max_id_query="SELECT ISNULL(MAX(ID), 0) FROM $DB_TABLE_NAME;"
    local max_id=$(sqlcmd -S "$DB_HOST,$DB_PORT" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -C -h -1 -W -Q "SET NOCOUNT ON; $max_id_query" 2>&1 | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    
    if ! [[ "$max_id" =~ ^[0-9]+$ ]]; then
        max_id=0
    fi
    
    echo $((max_id + 1))
}

# Function to add IP to database with ID
add_ip_to_db() {
    local ip=$1
    local action=$2
    local add_type=$3
    local date=$4
    local id=$5
    
    # Escape single quotes in values
    ip_escaped="${ip//\'/\'\'}"
    action_escaped="${action//\'/\'\'}"
    add_type_escaped="${add_type//\'/\'\'}"
    date_escaped="${date//\'/\'\'}"
    
    # Insert into database with ID
    local insert_query="INSERT INTO $DB_TABLE_NAME (ID, IP, action, dateAdded, addType) VALUES ($id, '$ip_escaped', '$action_escaped', '$date_escaped', '$add_type_escaped');"
    
    local result=$(sqlcmd -S "$DB_HOST,$DB_PORT" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -C -h -1 -W -Q "SET NOCOUNT ON; $insert_query" 2>&1)
    
    if echo "$result" | grep -qi "Msg [0-9]"; then
        echo "  - ERROR: Failed to insert into database: $result"
        return 1
    fi
    
    return 0
}

# Function to check if IP exists in database
check_ip_in_db() {
    local ip=$1
    ip_escaped="${ip//\'/\'\'}"
    
    local check_query="SELECT COUNT(*) FROM $DB_TABLE_NAME WHERE IP = '$ip_escaped';"
    local count=$(sqlcmd -S "$DB_HOST,$DB_PORT" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -C -h -1 -W -Q "SET NOCOUNT ON; $check_query" 2>&1 | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    
    if [ "$count" -gt 0 ]; then
        return 0  # IP exists
    else
        return 1  # IP does not exist
    fi
}

# Function to add IP to JSON file
add_ip_to_json() {
    local ip=$1
    local highest_id=$2
    local date=$3
    local add_type=$4
    local json_content=$5
    
    highest_id=$((highest_id + 1))
    
    json_content=$(echo "$json_content" | jq --arg ip "$ip" \
        --arg id "$highest_id" \
        --arg date "$date" \
        --arg add_type "$add_type" \
        '.[$ip] = {
            "Id": ($id | tonumber),
            "IpAddress": $ip,
            "Action": "block",
            "DateAdded": $date,
            "addType": $add_type
        }')
    
    echo "$json_content"
}

# Function to validate IP address (IPv4 only)
validate_ip() {
    local ip=$1
    
    # Check if empty
    if [ -z "$ip" ]; then
        return 1
    fi
    
    # Split by dots
    IFS='.' read -ra parts <<< "$ip"
    
    # Must have exactly 4 parts
    if [ ${#parts[@]} -ne 4 ]; then
        return 1
    fi
    
    # Check each part
    for part in "${parts[@]}"; do
        # Must be a number
        if ! [[ "$part" =~ ^[0-9]+$ ]]; then
            return 1
        fi
        
        # Must be between 0 and 255
        if [ "$part" -lt 0 ] || [ "$part" -gt 255 ]; then
            return 1
        fi
    done
    
    return 0
}

echo "Checking for IPs with more than $HIT_THRESHOLD hits in the $HOUR_TO_COUNT hour.s."

# Execute query using sqlcmd (MS SQL Server command-line tool)
# -C flag disables certificate validation
IPS=$(sqlcmd -S "$DB_HOST,$DB_PORT" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -C -h -1 -W -Q "SET NOCOUNT ON; $QUERY" 2>&1)

# Check for SQL errors
if echo "$IPS" | grep -qi "Msg [0-9]"; then
    echo "ERROR: SQL query failed:"
    echo "$IPS"
    exit 1
fi

# Clean up the output
IPS=$(echo "$IPS" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')

if [ -z "$IPS" ]; then
    echo "No IPs found exceeding threshold."
fi

# Count total IPs found
TOTAL_IPS=$(echo "$IPS" | grep -v '^$' | wc -l)
echo "Found $TOTAL_IPS IP(s) exceeding threshold:"
echo "----------------------------------------"

# Display sample of IPs that will be processed
echo "$IPS" | head -10 | while IFS=$'\t' read -r ip hit_count; do
    [ -z "$ip" ] && continue
    echo "  $ip - $hit_count hits"
done

if [ "$TOTAL_IPS" -gt 10 ]; then
    echo "  ... and $((TOTAL_IPS - 10)) more"
fi
echo "----------------------------------------"
echo ""

# Initialize JSON variables if using JSON file
if [ "$USE_DB_FOR_LOOKUP" = false ]; then
    # Read existing JSON file
    if [ ! -f "$JSON_FILE" ]; then
        echo "{}" > "$JSON_FILE"
    fi

    # Try to read JSON and validate it
    if ! JSON_CONTENT=$(cat "$JSON_FILE" | jq '.' 2>/dev/null); then
        echo "WARNING: JSON file is corrupted. Initializing with empty object."
        JSON_CONTENT="{}"
        echo "{}" > "$JSON_FILE"
    fi

    # Get the highest ID from existing JSON
    HIGHEST_ID=$(echo "$JSON_CONTENT" | jq -r '[.[].Id] | max // 0')
else
    # Get the highest ID from database
    HIGHEST_ID=$(get_next_id)
    HIGHEST_ID=$((HIGHEST_ID - 1))  # Subtract 1 since we'll increment before use
fi

# Process each IP using process substitution to avoid subshell issues
NEW_BLOCKS=0
while IFS=$'\t' read -r ip hit_count; do
    # Skip if IP is empty
    [ -z "$ip" ] && continue
    
    # Validate IP address format
    if ! validate_ip "$ip"; then
        echo "Processing IP: $ip (Hits: $hit_count)"
        echo "  - INVALID IP FORMAT, skipping..."
        continue
    fi
    
    echo "Processing IP: $ip (Hits: $hit_count)"
    
    # Check if IP already exists in conf file
    if grep -q "deny $ip;" "$CONF_FILE" 2>/dev/null; then
        echo "  - IP $ip already blocked in conf file, skipping..."
        continue
    fi
    
    # Check if IP already exists (DB or JSON based on config)
    if [ "$USE_DB_FOR_LOOKUP" = true ]; then
        if check_ip_in_db "$ip"; then
            echo "  - IP $ip already exists in database, skipping..."
            continue
        fi
    else
        if echo "$JSON_CONTENT" | jq -e --arg ip "$ip" '.[$ip]' > /dev/null 2>&1; then
            echo "  - IP $ip already exists in JSON file, skipping..."
            continue
        fi
    fi
    
    # Add to conf file
    echo "deny $ip;" >> "$CONF_FILE"
    echo "  - Added deny rule to $CONF_FILE"
    
    # Add to database or JSON based on config
    if [ "$USE_DB_FOR_LOOKUP" = true ]; then
        HIGHEST_ID=$((HIGHEST_ID + 1))
        if add_ip_to_db "$ip" "block" "automatic" "$CURRENT_TIME" "$HIGHEST_ID"; then
            echo "  - Added entry to database with ID $HIGHEST_ID"
            NEW_BLOCKS=$((NEW_BLOCKS + 1))
        fi
    else
        JSON_CONTENT=$(add_ip_to_json "$ip" "$HIGHEST_ID" "$CURRENT_TIME" "automatic" "$JSON_CONTENT")
        HIGHEST_ID=$((HIGHEST_ID + 1))
        echo "  - Added entry to JSON with ID $HIGHEST_ID"
        NEW_BLOCKS=$((NEW_BLOCKS + 1))
    fi
done < <(echo "$IPS")

# Write updated JSON back to file if using JSON
if [ "$USE_DB_FOR_LOOKUP" = false ]; then
    echo "$JSON_CONTENT" | jq '.' > "$JSON_FILE"
fi

# Add malicious IPs
LIST_URL="https://cinsscore.com/list/ci-badguys.txt"
TEMP_FILE="/tmp/malicious_ips_temp.txt"
JSON_TMP="/tmp/json_updates.$$.tmp"
CONF_TMP="/tmp/conf_updates.$$.tmp"
PROGRESS_FILE="/tmp/malicious_progress.$$.count"

echo ""
echo "========================================"
echo "Processing Malicious IPs"
echo "Deny IP API: $LIST_URL"
echo "========================================"
echo ""

> "$JSON_TMP"
> "$CONF_TMP"
> "$PROGRESS_FILE"

echo "Downloading malicious IP list from: $LIST_URL"
if curl -sS "$LIST_URL" | grep -v '^#' | grep -v '^$' > "$TEMP_FILE"; then
    if [ -s "$TEMP_FILE" ]; then
        TOTAL_IPS=$(wc -l < "$TEMP_FILE")
        echo "Downloaded $TOTAL_IPS malicious IPs"
        echo "----------------------------------------"

        if [ "$USE_DB_FOR_LOOKUP" = true ]; then
            # Database mode - process IPs directly
            # Create a file to track the current ID counter
            ID_COUNTER_FILE="/tmp/id_counter.$.tmp"
            echo "$HIGHEST_ID" > "$ID_COUNTER_FILE"
            
            export CONF_FILE CONF_TMP DB_HOST DB_PORT DB_NAME DB_USER DB_PASS DB_TABLE_NAME CURRENT_TIME PROGRESS_FILE TOTAL_IPS ID_COUNTER_FILE
            export -f validate_ip add_ip_to_db check_ip_in_db

            echo "Writing Malicious IP with ${MALICIOUS_IP_THREADS} Threads (DB mode)"
            cat "$TEMP_FILE" | xargs -P $MALICIOUS_IP_THREADS -I{} bash -c '
                ip="{}"
                [ -z "$ip" ] && exit 0
                if ! validate_ip "$ip"; then exit 0; fi
                if grep -q "deny $ip;" "$CONF_FILE" 2>/dev/null; then exit 0; fi
                if check_ip_in_db "$ip"; then exit 0; fi

                # Get next ID atomically
                (
                    flock 8
                    NEXT_ID=$(cat "$ID_COUNTER_FILE")
                    NEXT_ID=$((NEXT_ID + 1))
                    echo "$NEXT_ID" > "$ID_COUNTER_FILE"
                    
                    echo "deny $ip;" >> "$CONF_TMP"
                    
                    if add_ip_to_db "$ip" "block" "malicious" "$CURRENT_TIME" "$NEXT_ID"; then
                        # atomic progress
                        (
                            flock 9
                            COUNT=$(($(cat "$PROGRESS_FILE") + 1))
                            echo "$COUNT" > "$PROGRESS_FILE"
                            PCT=$(( COUNT * 100 / TOTAL_IPS ))
                            if (( COUNT % 50 == 0 || COUNT == TOTAL_IPS )); then
                                echo "DB insert progress: $COUNT / $TOTAL_IPS ($PCT%)"
                            fi
                        ) 9>"$PROGRESS_FILE.lock"
                    fi
                ) 8>"$ID_COUNTER_FILE.lock"
            '
            
            cat "$CONF_TMP" >> "$CONF_FILE"
            
            # Update HIGHEST_ID for the main script
            HIGHEST_ID=$(cat "$ID_COUNTER_FILE")
            rm -f "$ID_COUNTER_FILE" "$ID_COUNTER_FILE.lock"
            
        else
            # JSON mode - original logic
            export CONF_FILE JSON_FILE JSON_TMP CONF_TMP CURRENT_TIME HIGHEST_ID PROGRESS_FILE TOTAL_IPS
            export -f validate_ip

            echo "Writing Malicious IP with ${MALICIOUS_IP_THREADS} Threads (JSON mode)"
            cat "$TEMP_FILE" | xargs -P $MALICIOUS_IP_THREADS -I{} bash -c '
                ip="{}"
                [ -z "$ip" ] && exit 0
                if ! validate_ip "$ip"; then exit 0; fi
                if grep -q "deny $ip;" "$CONF_FILE" 2>/dev/null; then exit 0; fi
                if jq -e --arg ip "$ip" ".[$ip]" "$JSON_FILE" >/dev/null 2>&1; then exit 0; fi

                echo "deny $ip;" >> "$CONF_TMP"
                printf "%s\n" "$ip" >> "$JSON_TMP"

                # atomic progress
                (
                    flock 9
                    COUNT=$(($(cat "$PROGRESS_FILE") + 1))
                    echo "$COUNT" > "$PROGRESS_FILE"
                    PCT=$(( COUNT * 100 / TOTAL_IPS ))
                    if (( COUNT % 50 == 0 || COUNT == TOTAL_IPS )); then
                        echo "Staging progress: $COUNT / $TOTAL_IPS ($PCT%)"
                    fi
                ) 9>"$PROGRESS_FILE.lock"
            '

            echo ""
            echo "Merging staged IPs into JSON and conf files..."

            # Merge conf updates
            cat "$CONF_TMP" >> "$CONF_FILE"

            # After parallel staging, merge JSON in main shell
            TOTAL_JSON=$(wc -l < "$JSON_TMP")
            COUNT_JSON=0
            while read -r ip; do
                [ -z "$ip" ] && continue
                HIGHEST_ID=$((HIGHEST_ID + 1))
                JSON_CONTENT=$(echo "$JSON_CONTENT" | jq --arg ip "$ip" \
                    --arg id "$HIGHEST_ID" \
                    --arg date "$CURRENT_TIME" \
                    '.[$ip] = {
                        "Id": ($id | tonumber),
                        "IpAddress": $ip,
                        "Action": "block",
                        "DateAdded": $date,
                        "addType": "malicious"
                    }')
                COUNT_JSON=$((COUNT_JSON + 1))
                if (( COUNT_JSON % 50 == 0 || COUNT_JSON == TOTAL_JSON )); then
                    PCT=$(( COUNT_JSON * 100 / TOTAL_JSON ))
                    echo "JSON merge progress: $COUNT_JSON / $TOTAL_JSON ($PCT%)"
                fi
            done < "$JSON_TMP"
            
            # Finally, write JSON_CONTENT to file
            echo "$JSON_CONTENT" | jq '.' > "$JSON_FILE"
        fi

        echo "----------------------------------------"
        ADDED_COUNT=$(cat "$PROGRESS_FILE" 2>/dev/null || echo "0")
        echo "Malicious IPs processed: $ADDED_COUNT new blocks added"

        rm -f "$TEMP_FILE" "$JSON_TMP" "$CONF_TMP" "$PROGRESS_FILE" "$PROGRESS_FILE.lock"
    else
        echo "ERROR: Downloaded file is empty"
    fi
else
    echo "ERROR: Failed to download malicious IP list"
fi

echo ""
echo "Script completed successfully!"
echo "New IPs blocked: $NEW_BLOCKS"
echo "Updated files:"
echo "  - $CONF_FILE"
if [ "$USE_DB_FOR_LOOKUP" = true ]; then
    echo "  - Database: $DB_TABLE_NAME table"
else
    echo "  - $JSON_FILE"
fi
echo "Backups saved to: $BACKUP_DIR"
