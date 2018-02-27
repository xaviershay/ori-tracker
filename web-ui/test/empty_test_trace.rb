require 'google/cloud/firestore'
firestore = Google::Cloud::Firestore.new(project_id: 'ori-tracker', keyfile: File.expand_path("../../secrets/ori-tracker-admin.json", __FILE__))
