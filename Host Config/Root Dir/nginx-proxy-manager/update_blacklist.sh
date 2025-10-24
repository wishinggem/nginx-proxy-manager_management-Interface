#!/bin/bash
NPM_DIR="/{Root Dir}/nginx-proxy-manager"

HTTP_CONF="$NPM_DIR/data/nginx/custom/http.conf"
STREAM_CONF="$NPM_DIR/data/nginx/custom/stream.conf"

LOG_FILE="$NPM_DIR/update_blacklist.log"

MANUAL_RULES_FILE="/{Root Dir}/npm-management/host_data/ips.conf"

log() {
    local message="$1"
    echo "$message" | tee -a "$LOG_FILE"
}

log "NPM | Auto Update Blacklist System"
log " "
log "--------Started Blacklist Update at $(date)--------"
log " "
log " http.conf: $HTTP_CONF"
log " stream.conf: $STREAM_CONF"
log " Block IP List: $MANUAL_RULES_FILE"
log " Log File Path: $LOG_FILE"
log " "
log "----------------------------------------"
log " "

log "Coppying IPs to http.conf"

sudo cp -f "$MANUAL_RULES_FILE" "$HTTP_CONF"

log "Copying IPs to stream.conf"

sudo cp -f "$MANUAL_RULES_FILE" "$STREAM_CONF"

log "Restarting Nginx Proxy Manager Docker Container"
docker compose restart npm
log "Successfully updated IP blacklists and restarted NPM."

log "--------Finished Blacklist update at $(date)--------"
