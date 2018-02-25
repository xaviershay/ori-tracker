
#require 'firebase'
#base_uri = 'https://ori-tracker.firebaseio.com/'
#
#firebase = Firebase::Client.new(base_uri, File.read(File.expand_path("../../secrets/ori-tracker-firebase-adminsdk-4o4a2-e0345f534e.json", __FILE__)))

require 'google/cloud/firestore'
firestore = Google::Cloud::Firestore.new(project_id: 'ori-tracker', keyfile: File.expand_path("../../secrets/ori-tracker-admin.json", __FILE__))
now = Time.now.to_f

data = [
  { x: 109.90181, y: -257.350216, timestamp: now },
  { x: 108, y: -257.350216, timestamp: now + 0.2 },
  { x: 107, y: -257.350216, timestamp: now + 0.4 },
]
data.each do |t|
  d = firestore.doc("boards/abc123/players/xavier/traces/#{(t[:timestamp] * 1000).to_i}")
  p d.set(t)
end
