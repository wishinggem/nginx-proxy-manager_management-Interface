# nginx-proxy-manager_management-Interface

Prerequisites

1. Docker and Docker Compose (v2) are installed on the host device.

File System Structure:
  There is a file system structure under Host Config/Root Dir.
  This structure must be mirrored for the interface to work correctly.

After copying the structure and filling out the relevant data, you can run:

  docker compose up -d

This will automatically create all required directories.

You may want to schedule the included bash scripts using cron. For example:

    0 4 * * * /bin/bash /{Root Dir}/nginx-proxy-manager/update-blacklist.sh >> /dev/null 2>&1
    */30 * * * * /{Root Dir}/nginx-proxy-manager/combine_logs.sh >> /dev/null 2>&1
    0 0 * * * /{Root Dir}/npm-management/update_rpConnections_db.sh >> /{Root Dir}/npm-management/update_rpConnections_db.log 2>&1
    0 0 * * * /{Root Dir}/npm-management/update_manualblacklist_from_sql.sh >> /{Root Dir}/npm-management/update_blacklist_from_sql.log

Add these under the sudo crontab using:
  
  sudo crontab -e

After adding the cron jobs, run the scripts in the following order once:

1. combine_logs.sh
2. update_rpConnections_db.sh
3. update_manualblacklist_from_sql.sh
4. update-blacklist.sh

This will initialize automatic operations.

MSSQL Database (Optional)
If you do not want to use a MSSQL database: Set ServiceVariables__useDB to false.
  Manual and malicious blocks will work.
  Automatic blocks (from SQL queries) will not be added.

There are three types of IP blocks:
- Manual: Added via the interface.
- Automatic: Determined by SQL queries, based on X amount of request within a set timeframe.
- Malicious: Known malicious IPs fetched from an external API.

<img width="1031" height="1248" alt="image" src="https://github.com/user-attachments/assets/b9247a49-21a6-4bd5-8bfc-7343de8b1695" />
<img width="1091" height="652" alt="image" src="https://github.com/user-attachments/assets/3ed012c8-7bd8-46ae-b037-7706ffa1c0f1" />
