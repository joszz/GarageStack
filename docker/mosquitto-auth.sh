#!/bin/sh
# Writes the Mosquitto password and ACL files into the directory given as $1. Shared by the
# mosquitto service in docker-compose.yml and the all-in-one image's entrypoint, and run on
# every start so changed credentials always take effect.
#
#   MQTT_BROKER_USERNAME / MQTT_BROKER_PASSWORD  Internal login for the SAIC gateway, Api and
#                                                Worker. Full access.
#   HA_MQTT_USERNAME / HA_MQTT_PASSWORD          Optional Home Assistant login, skipped when
#                                                HA_MQTT_USERNAME is empty. See documentation/HOME_ASSISTANT.md.
set -eu

dir="${1:?Usage: mosquitto-auth.sh <directory>}"
passwd_file="$dir/passwd"
acl_file="$dir/acl"

: "${MQTT_BROKER_USERNAME:?MQTT_BROKER_USERNAME is required}"
: "${MQTT_BROKER_PASSWORD:?MQTT_BROKER_PASSWORD is required}"
HA_MQTT_USERNAME="${HA_MQTT_USERNAME:-}"
HA_MQTT_PASSWORD="${HA_MQTT_PASSWORD:-}"

rm -f "$passwd_file" "$acl_file"
mosquitto_passwd -b -c "$passwd_file" "$MQTT_BROKER_USERNAME" "$MQTT_BROKER_PASSWORD"
printf 'user %s\ntopic readwrite #\n' "$MQTT_BROKER_USERNAME" > "$acl_file"

if [ -n "$HA_MQTT_USERNAME" ]; then
    if [ -z "$HA_MQTT_PASSWORD" ]; then
        echo "HA_MQTT_PASSWORD is required when HA_MQTT_USERNAME is set" >&2
        exit 1
    fi
    if [ "$HA_MQTT_USERNAME" = "$MQTT_BROKER_USERNAME" ]; then
        echo "HA_MQTT_USERNAME must differ from MQTT_BROKER_USERNAME" >&2
        exit 1
    fi
    mosquitto_passwd -b "$passwd_file" "$HA_MQTT_USERNAME" "$HA_MQTT_PASSWORD"
    # saic/#: gateway telemetry plus the /set command topics. homeassistant/#: the gateway's
    # retained discovery configs. homeassistant/status: Home Assistant's own online/offline
    # message, which makes the gateway re-publish discovery after an HA restart.
    printf '\nuser %s\ntopic readwrite saic/#\ntopic read homeassistant/#\ntopic write homeassistant/status\n' \
        "$HA_MQTT_USERNAME" >> "$acl_file"
fi

# Mosquitto 2.x refuses to load these files unless they are owned by the "mosquitto" account
chown mosquitto:mosquitto "$passwd_file" "$acl_file"
chmod 600 "$passwd_file" "$acl_file"
