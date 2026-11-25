// Package license calls the Lemon Squeezy License API.
package license

import (
	"encoding/json"
	"net/http"
	"net/url"
	"strings"
	"time"
)

const licenseAPI = "https://licenses.exisone.com/v1/licenses"

var client = &http.Client{Timeout: 10 * time.Second}

func call(action string, params url.Values) (map[string]any, error) {
	req, err := http.NewRequest(http.MethodPost, licenseAPI+"/"+action, strings.NewReader(params.Encode()))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	res, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer res.Body.Close()
	var body map[string]any // failures carry an "error" message
	err = json.NewDecoder(res.Body).Decode(&body)
	return body, err
}

func Activate(key, instanceName string) (map[string]any, error) {
	return call("activate", url.Values{"license_key": {key}, "instance_name": {instanceName}})
}

func Validate(key, instanceID string) (map[string]any, error) {
	p := url.Values{"license_key": {key}}
	if instanceID != "" {
		p.Set("instance_id", instanceID)
	}
	return call("validate", p)
}

func Deactivate(key, instanceID string) (map[string]any, error) {
	return call("deactivate", url.Values{"license_key": {key}, "instance_id": {instanceID}})
}
