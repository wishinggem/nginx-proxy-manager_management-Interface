#!/bin/bash

# --- Configuration ---
NPM_DIR="/{Root Dir}/nginx-proxy-manager"

# Define the log directory
LOG_DIR="$NPM_DIR/data/logs"

# Define the destination file name
DEST_FILE="$LOG_DIR/proxy-host-*_access.log"

# Script log file path
LOG_FILE="$NPM_DIR/combined_logs.log"

# --- Function to log messages ---
log() {
    local message="$1"
    echo "$message" | tee -a "$LOG_FILE"
}

# --- Script Logic ---
log "NPM | Auto Combine Log System"
log " "
log "--------Started Auto Combine at $(date)--------"
log " "
log " NPM Log dir: $LOG_DIR"
log " DESTINATION FILE: $DEST_FILE"
log " Log File Path: $LOG_FILE"
log " "
log "----------------------------------------"
log " "

# Temporary file to ensure atomic write
TMP_FILE="$(mktemp)"

# Combine all matching logs
if ls "$LOG_DIR"/proxy-host-[0-9]*_access.log 1> /dev/null 2>&1; then
    cat "$LOG_DIR"/proxy-host-[0-9]*_access.log > "$TMP_FILE"
    mv "$TMP_FILE" "$DEST_FILE"
    chmod 644 "$DEST_FILE"
    log "[SUCCESS] Combined log files into $DEST_FILE"
    log "--- Tail of combined file ---"
    tail -n 10 "$DEST_FILE" | tee -a "$LOG_FILE"
    log "-----------------------------"
else
    log "[WARNING] No matching log files found in $LOG_DIR"
    rm -f "$TMP_FILE"
fi

log "--------Finished at $(date)--------"
log " "
