const functions = require('firebase-functions');
const admin = require('firebase-admin');

admin.initializeApp(functions.config().firebase);
var db = admin.firestore()

// // Create and Deploy Your First Cloud Functions
// // https://firebase.google.com/docs/functions/write-firebase-functions
//
exports.helloWorld = functions.https.onRequest((request, response) => {
  response.send("Hello from Firebase!");
});

// TODO: Validate data
exports.track = functions.https.onRequest((req, response) => {
  var batch = db.batch();
  req.body.forEach(function(x) {
    var board = req.query.board_id
    var player = req.query.player_id
    var id = x.timestamp
    var doc = {
      x: x.x,
      y: x.y
    }

    var path = "boards/" + board + "/players/" + player + "/traces/" + id

    batch.set(db.doc(path), doc);
  })

  return batch.commit().then(function() {
    return response.send("Ok");
  })
});
