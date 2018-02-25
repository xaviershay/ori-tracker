const functions = require('firebase-functions');
const admin = require('firebase-admin');

admin.initializeApp(functions.config().firebase);

// // Create and Deploy Your First Cloud Functions
// // https://firebase.google.com/docs/functions/write-firebase-functions
//
exports.helloWorld = functions.https.onRequest((request, response) => {
  response.send("Hello from Firebase!");
});

exports.track = functions.https.onRequest((req, response) => {
  var board = req.query.board_id
  var player = req.query.player_id
  var id = req.query.timestamp
  var doc = {
    x: req.query.x,
    y: req.query.y
  }
  var path = "boards/" + board + "/players/" + player + "/traces/" + id

  return admin.firestore().doc(path).set(doc).then((writeResult) => {
    // TODO: Check for success
    return response.send("Set " + path + "  => " + writeResult);
  });

  // TODO: Validate data
});
