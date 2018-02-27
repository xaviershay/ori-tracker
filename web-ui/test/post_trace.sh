#!/bin/bash

exec curl -vX POST "http://localhost:5001/ori-tracker/us-central1/track?board_id=abc123&player_id=xavier" -d @test/fake_trace.json --header "Content-Type: application/json"
