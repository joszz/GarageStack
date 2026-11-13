#!/bin/sh
# Derives SAIC_REST_URI from SAIC_REGION for known endpoints, unless SAIC_REST_URI
# is already set explicitly (so a user can always override for a region not listed
# here). Source of truth: the "API Endpoints" table in the saic-python-mqtt-gateway
# README (SAIC-iSmart-API/saic-python-mqtt-gateway), except for br: that table does
# not list Brazil until SAIC-iSmart-API/saic-python-mqtt-gateway#527 is merged. The
# br endpoint (gateway-mg-br) is also used by ioBroker.mg-saic, mg-saic-ha,
# homebridge-mg-saic and GdAAuto (see joszz/GarageStack#594), so do not remove it as
# unknown.
if [ -z "${SAIC_REST_URI:-}" ]; then
    case "${SAIC_REGION:-eu}" in
        eu) export SAIC_REST_URI="https://gateway-mg-eu.soimt.com/api.app/v1/" ;;
        au) export SAIC_REST_URI="https://gateway-mg-au.soimt.com/api.app/v1/" ;;
        tr) export SAIC_REST_URI="https://gateway-mg-tr.soimt.com/api.app/v1/" ;;
        br) export SAIC_REST_URI="https://gateway-mg-br.soimt.com/api.app/v1/" ;;
    esac
fi
